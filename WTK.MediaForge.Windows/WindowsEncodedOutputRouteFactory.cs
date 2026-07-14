using Vortice.DXGI;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Composition.Runtime.Scheduling;
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

    public WindowsEncodedOutputRouteFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>>? capabilityReportFactory = null,
        bool allowUnvalidatedRoutes = false)
    {
        _diagnostics = diagnostics;
        _capabilityReportFactory = capabilityReportFactory ?? MediaForgeWindows.GetCapabilityReportWithHardwareProofsAsync;
        _allowUnvalidatedRoutes = allowUnvalidatedRoutes;
    }

    public bool CanCreate(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.RecordingMp4 ||
        typeId == RenderOutputTypes.StreamingRtmp;

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

        await EnsureCapabilityAllowsRouteAsync(output, cancellationToken).ConfigureAwait(false);

        var audit = new CollectingMediaTransportAuditSink();
        var routeResources = WindowsEncodedOutputRouteResources.Create();
        IHardwareVideoEncoder? encoder = null;
        var accepted = false;
        try
        {
            var encoderSettings = CreateEncoderSettings(output);
            encoder = new MediaFoundationHardwareVideoEncoder(
                routeResources.Device.Device,
                encoderSettings);

            var frameAdapter = new RenderedOutputEncodeFrameAdapter(
                new RenderedOutputEncoderInputPreparer(
                    new WindowsRenderedOutputEncoderSurfaceExporter(routeResources.Device.Device),
                    new WindowsRenderedOutputEncoderInputConverter(routeResources.Device.Device)));

            var sink = CreateSink(output, audit);
            await runtime.RegisterEncodedOutputAsync(
                output.Id,
                frameAdapter,
                encoder,
                routeResources.FrameExporter,
                CreateSinkContext(output, encoderSettings),
                [sink],
                audit,
                backpressurePolicy: EncodedOutputBackpressurePolicy.ForOutputType(output.TypeId),
                routeResources: routeResources,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            accepted = true;
        }
        finally
        {
            if (!accepted)
            {
                if (encoder is not null)
                    await encoder.DisposeAsync().ConfigureAwait(false);

                await routeResources.DisposeAsync().ConfigureAwait(false);
            }
        }
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
            PixelFormat = profile.PixelFormat
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

        public static WindowsEncodedOutputRouteResources Create()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Windows encoded output routes require Windows D3D11.");

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            var device = D3D11GpuDevice.CreateForAdapter(adapter);
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
