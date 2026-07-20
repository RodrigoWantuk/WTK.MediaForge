using Vortice.DXGI;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Interop;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsEncodedOutputRouteFactory : IEncodedOutputRouteFactory
{
    private readonly Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>> _capabilityReportFactory;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly bool _allowUnvalidatedRoutes;
    private readonly GpuAdapterAffinityState? _adapterAffinity;
    private readonly SemaphoreSlim _registrationGate = new(1, 1);
    private readonly HashSet<RenderOutputId> _registeredOutputIds = [];

    public WindowsEncodedOutputRouteFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>>? capabilityReportFactory = null,
        bool allowUnvalidatedRoutes = false,
        GpuAdapterAffinityState? adapterAffinity = null)
    {
        _diagnostics = diagnostics;
        _capabilityReportFactory = capabilityReportFactory ?? MediaForgeWindows.GetCapabilityReportWithHardwareProofsAsync;
        _allowUnvalidatedRoutes = allowUnvalidatedRoutes;
        _adapterAffinity = adapterAffinity;
    }

    public bool CanCreate(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.RecordingMp4 ||
        typeId == RenderOutputTypes.StreamingRtmp;

    public RenderOutputId ResolveSurfaceOutputId(
        MediaForgeProject project,
        MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);
        return GetCompatibleOutputs(project, output)[0].Id;
    }

    public async ValueTask RegisterAsync(
        MediaForgeProject project,
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(runtime);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanCreate(output.TypeId))
        {
            throw new ArgumentException(
                $"Output type '{output.TypeId.Value}' is not an encoded Windows output route.",
                nameof(output));
        }

        await _registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_registeredOutputIds.Contains(output.Id) && runtime.IsEncodedOutputRegistered(output.Id))
                return;

            if (_registeredOutputIds.Contains(output.Id))
            {
                foreach (var groupedOutput in GetCompatibleOutputs(project, output))
                    _registeredOutputIds.Remove(groupedOutput.Id);
            }

            var groupedOutputs = GetCompatibleOutputs(project, output);
            foreach (var groupedOutput in groupedOutputs)
                await EnsureCapabilityAllowsRouteAsync(groupedOutput, cancellationToken).ConfigureAwait(false);

            var surfaceOutput = groupedOutputs[0];
            var audit = new CollectingMediaTransportAuditSink();
            var routeResources = WindowsEncodedOutputRouteResources.Create(_adapterAffinity);
            IHardwareVideoEncoder? encoder = null;
            var sinks = new List<IEncodedPacketSink>(groupedOutputs.Count);
            var ownershipTransferred = false;
            try
            {
                var encoderSettings = CreateEncoderSettings(surfaceOutput);
                encoder = new MediaFoundationHardwareVideoEncoder(
                    routeResources.Device.Device,
                    encoderSettings);

                var frameAdapter = new RenderedOutputEncodeFrameAdapter(
                    new RenderedOutputEncoderInputPreparer(
                        new WindowsRenderedOutputEncoderSurfaceExporter(routeResources.Device.Device),
                        new WindowsRenderedOutputEncoderInputConverter(routeResources.Device.Device)));

                var registrations = new List<EncodedOutputSinkRegistration>(groupedOutputs.Count);
                foreach (var groupedOutput in groupedOutputs)
                {
                    var sink = CreateSink(groupedOutput, audit);
                    sinks.Add(sink);
                    registrations.Add(new EncodedOutputSinkRegistration(
                        groupedOutput.Id,
                        sink,
                        EncodedOutputBackpressurePolicy.ForOutputType(groupedOutput.TypeId)));
                }

                ownershipTransferred = true;
                await runtime.RegisterEncodedOutputGroupAsync(
                    surfaceOutput.Id,
                    frameAdapter,
                    encoder,
                    routeResources.FrameExporter,
                    CreateSinkContext(surfaceOutput, encoderSettings),
                    registrations,
                    audit,
                    routeResources: routeResources,
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                foreach (var groupedOutput in groupedOutputs)
                    _registeredOutputIds.Add(groupedOutput.Id);
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    if (encoder is not null)
                        await encoder.DisposeAsync().ConfigureAwait(false);

                    foreach (var sink in sinks)
                        await sink.DisposeAsync().ConfigureAwait(false);

                    await routeResources.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _registrationGate.Release();
        }
    }

    public async ValueTask RecreateAsync(
        MediaForgeProject project,
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(runtime);

        var groupedOutputs = GetCompatibleOutputs(project, output);
        await _registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await runtime
                .UnregisterEncodedOutputAsync(output.Id, timeout, cancellationToken)
                .ConfigureAwait(false);
            foreach (var groupedOutput in groupedOutputs)
                _registeredOutputIds.Remove(groupedOutput.Id);
        }
        finally
        {
            _registrationGate.Release();
        }

        await RegisterAsync(project, output, runtime, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<MediaForgeRenderOutput> GetCompatibleOutputs(
        MediaForgeProject project,
        MediaForgeRenderOutput output)
    {
        if (!CanCreate(output.TypeId))
        {
            throw new ArgumentException(
                $"Output type '{output.TypeId.Value}' is not an encoded Windows output route.",
                nameof(output));
        }

        var key = EncodedRouteCompatibilityKey.Create(output);
        if (!project.Outputs.Any(candidate => candidate.Id == output.Id))
            throw new InvalidOperationException($"Encoded output '{output.Name}' was not found in its project.");

        var compatible = project.Outputs
            .Where(candidate => CanCreate(candidate.TypeId))
            .Where(candidate => EncodedRouteCompatibilityKey.Create(candidate) == key)
            .ToArray();
        if (compatible.Length == 0)
            throw new InvalidOperationException($"Encoded output '{output.Name}' was not found in its project.");

        return compatible;
    }

    private async ValueTask EnsureCapabilityAllowsRouteAsync(
        MediaForgeRenderOutput output,
        CancellationToken cancellationToken)
    {
        if (_allowUnvalidatedRoutes)
            return;

        var capabilityId = output.TypeId == RenderOutputTypes.RecordingMp4
            ? MediaForgeCapabilityCatalog.RecordingMp4H264
            : MediaForgeCapabilityCatalog.RtmpH264;

        var report = await _capabilityReportFactory(cancellationToken).ConfigureAwait(false);
        var capability = report.TryGetEntry(capabilityId);
        if (capability?.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental)
            return;

        var reason = capability?.UnavailableReason ??
                     "Windows encoded output route requires product media proofs before it can start.";
        var exception = new MediaForgeUnsupportedFeatureException(capabilityId, reason);
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Error,
            "engine.encoded_output_route_unavailable",
            reason,
            nameof(WindowsEncodedOutputRouteFactory),
            exception,
            output.Id.Value,
            output.Name);

        throw exception;
    }

    private static IEncodedPacketSink CreateSink(
        MediaForgeRenderOutput output,
        IMediaTransportAuditSink audit)
    {
        if (output.TypeId == RenderOutputTypes.RecordingMp4)
        {
            var settings = (RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(
                output.TypeId,
                output.Settings);
            return new RecordingMp4PacketSink(settings.Path, audit);
        }

        if (output.TypeId == RenderOutputTypes.StreamingRtmp)
        {
            var settings = (StreamingRtmpOutputSettings)RenderOutputSettingsSerializer.Deserialize(
                output.TypeId,
                output.Settings);
            return new RtmpPacketSink(CombineRtmpUrl(settings.Url, settings.StreamKey));
        }

        throw new ArgumentOutOfRangeException(nameof(output), output.TypeId.Value, "Unsupported encoded output route.");
    }

    private static EncodedPacketSinkContext CreateSinkContext(
        MediaForgeRenderOutput output,
        HardwareVideoEncoderSettings settings) =>
        new()
        {
            Codec = EncodedVideoCodec.H264,
            Size = output.OutputSize,
            FramesPerSecond = settings.FramesPerSecond
        };

    private static HardwareVideoEncoderSettings CreateEncoderSettings(MediaForgeRenderOutput output)
    {
        var size = output.OutputSize;
        if (size.IsEmpty)
            throw new InvalidOperationException($"Encoded output '{output.Name}' requires a non-empty output size.");

        var profile = GetEncodedVideoProfile(output);
        ValidateEncodedVideoProfile(output, profile);

        return new HardwareVideoEncoderSettings
        {
            Width = checked((int)size.Width),
            Height = checked((int)size.Height),
            Codec = profile.Codec,
            FramesPerSecond = profile.FramesPerSecond,
            BitrateBitsPerSecond = profile.BitrateBitsPerSecond,
            KeyFrameIntervalFrames = profile.KeyFrameIntervalFrames,
            PixelFormat = profile.PixelFormat,
            H264Profile = profile.H264Profile,
            H264Level = profile.H264Level
        };
    }

    private static EncodedVideoProfile GetEncodedVideoProfile(MediaForgeRenderOutput output)
    {
        if (output.TypeId == RenderOutputTypes.RecordingMp4)
        {
            var settings = (RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(
                output.TypeId,
                output.Settings);
            return settings.Video;
        }

        if (output.TypeId == RenderOutputTypes.StreamingRtmp)
        {
            var settings = (StreamingRtmpOutputSettings)RenderOutputSettingsSerializer.Deserialize(
                output.TypeId,
                output.Settings);
            return settings.Video;
        }

        throw new ArgumentOutOfRangeException(nameof(output), output.TypeId.Value, "Unsupported encoded output route.");
    }

    private static void ValidateEncodedVideoProfile(
        MediaForgeRenderOutput output,
        EncodedVideoProfile profile)
    {
        try
        {
            if (profile.Codec != EncodedVideoCodec.H264)
                throw new NotSupportedException($"Encoded output '{output.Name}' supports H.264 only.");

            if (profile.FramesPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(profile.FramesPerSecond));

            if (profile.BitrateBitsPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(profile.BitrateBitsPerSecond));

            if (profile.KeyFrameIntervalFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(profile.KeyFrameIntervalFrames));

            ArgumentException.ThrowIfNullOrWhiteSpace(profile.PixelFormat);
            _ = HardwareVideoEncoderSettings.GetH264ProfileValue(profile.H264Profile);
            _ = HardwareVideoEncoderSettings.GetH264LevelValue(profile.H264Level);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Encoded output '{output.Name}' has an invalid video profile.",
                ex);
        }
    }

    private static string CombineRtmpUrl(string url, string streamKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);

        var trimmedUrl = url.TrimEnd('/');
        return trimmedUrl.EndsWith(streamKey, StringComparison.Ordinal)
            ? trimmedUrl
            : $"{trimmedUrl}/{streamKey}";
    }

    private sealed class WindowsEncodedOutputRouteResources : IAsyncDisposable
    {
        private WindowsEncodedOutputRouteResources(
            D3D11GpuDevice device,
            VulkanToD3D11EncoderSurfaceExporter frameExporter)
        {
            Device = device;
            FrameExporter = frameExporter;
        }

        public D3D11GpuDevice Device { get; }

        public VulkanToD3D11EncoderSurfaceExporter FrameExporter { get; }

        public static WindowsEncodedOutputRouteResources Create(GpuAdapterAffinityState? adapterAffinity)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Windows encoded output routes require Windows D3D11.");

            var device = WindowsD3D11AdapterSelector.CreateDevice(
                adapterAffinity,
                requireVideoSupport: true);
            return new WindowsEncodedOutputRouteResources(
                device,
                new VulkanToD3D11EncoderSurfaceExporter(device.Device));
        }

        public ValueTask DisposeAsync()
        {
            FrameExporter.Dispose();
            Device.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
