using System.Runtime.ExceptionServices;
using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Windows.Media.Decode;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal static class WindowsHardwareDecodeProofPipeline
{
    private static readonly TimeSpan ProofTimeout = TimeSpan.FromSeconds(5);

    public static async ValueTask<DecodedGpuFrame> DecodeGeneratedMp4FrameAsync(
        WindowsProductMp4ProofAsset asset,
        MediaFoundationHardwareVideoDecoder decoder,
        CollectingMediaTransportAuditSink audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(audit);

        await decoder
            .OpenAsync(
                new HardwareDecodeOpenContext
                {
                    SourcePath = asset.Path,
                    Session = new HardwareDecodeSession
                    {
                        Codec = EncodedVideoCodec.H264,
                        Width = asset.Width,
                        Height = asset.Height
                    },
                    CancellationToken = cancellationToken
                },
                audit)
            .ConfigureAwait(false);

        var decoded = await decoder
            .DecodeNextFrameAsync(
                new FileDecodeFrameContext
                {
                    FrameNumber = 1,
                    PresentationTime = TimeSpan.Zero,
                    CancellationToken = cancellationToken
                },
                audit)
            .ConfigureAwait(false);

        if (decoded is null)
            throw new NotSupportedException("Media Foundation D3D11VA did not return a decoded GPU frame.");

        if (!HasBackendValidatedDecode(audit.Events))
        {
            decoded.Dispose();
            throw new NotSupportedException(
                "Media Foundation D3D11VA decode did not produce BackendOutputValidated evidence.");
        }

        return decoded;
    }

    public static bool HasBackendValidatedDecode(IReadOnlyList<MediaTransportAuditEvent> events) =>
        events.Any(static e =>
            e.Kind == MediaTransportAuditEventKind.HardwareDecodeSucceeded &&
            e.EvidenceKind == MediaTransportAuditEvidenceKind.BackendOutputValidated);

    public static async ValueTask SubmitDecodedSourceFrameToRendererAsync(
        DecodedGpuFrame decoded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var sourceLease = DecodedFrameToSourceFrameAdapter.Instance
            .CreateSourceFrameLease(decoded, SourceId.New(), frameNumber: 1);
        await SubmitSourceFrameLeaseToRendererAsync(sourceLease, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask SubmitVideoFileProviderFrameToRendererAsync(
        WindowsProductMp4ProofAsset asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceId = SourceId.New();
        var provider = new WindowsVideoFileSourceProviderFactory(enableProductProvider: true)
            .CreateProvider(CreateVideoFileSourceDefinition(sourceId, asset.Path));
        try
        {
            await provider.StartAsync(cancellationToken).ConfigureAwait(false);
            var frameLease = await WaitForProviderFrameAsync(provider, cancellationToken).ConfigureAwait(false);
            await SubmitSourceFrameLeaseToRendererAsync(frameLease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await provider.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    public static async ValueTask SubmitWebcamProviderFrameToRendererAsync(
        WindowsWebcamDeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceId = SourceId.New();
        var provider = new WindowsWebcamSourceProviderFactory()
            .CreateProvider(CreateWebcamSourceDefinition(sourceId, device));
        try
        {
            await provider.StartAsync(cancellationToken)
                .WaitAsync(ProofTimeout, cancellationToken)
                .ConfigureAwait(false);
            var frameLease = await WaitForProviderFrameAsync(provider, cancellationToken).ConfigureAwait(false);
            await SubmitSourceFrameLeaseToRendererAsync(frameLease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await provider.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (provider is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    public static async ValueTask SubmitWindowCaptureProviderFrameToRendererAsync(
        nint windowHandle,
        CancellationToken cancellationToken)
    {
        if (windowHandle == 0)
            throw new ArgumentException("A valid proof window handle is required.", nameof(windowHandle));
        cancellationToken.ThrowIfCancellationRequested();

        var sourceId = SourceId.New();
        var provider = new WindowsWindowCaptureSourceProviderFactory()
            .CreateProvider(CreateWindowCaptureSourceDefinition(sourceId, windowHandle));
        try
        {
            await provider.StartAsync(cancellationToken)
                .WaitAsync(ProofTimeout, cancellationToken)
                .ConfigureAwait(false);
            var frameLease = await WaitForProviderFrameAsync(provider, cancellationToken).ConfigureAwait(false);
            await SubmitSourceFrameLeaseToRendererAsync(frameLease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await provider.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (provider is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private static async ValueTask SubmitSourceFrameLeaseToRendererAsync(
        GpuFrameLease sourceLease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceLease);

        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var guard = new RenderThreadGuard();
        guard.BindToCurrentThread();

        MediaForgeVulkanRenderer? renderer = null;
        IRenderFrameSubmission? submission = null;
        RenderFrameSnapshot? snapshot = null;
        var leaseTransferredToSnapshot = false;
        Exception? operationFailure = null;
        try
        {
            if (!MediaForgeVulkanRenderer.TryCreate(
                    guard,
                    diagnostics: null,
                    NullVulkanRendererFaultInjector.Instance,
                    out renderer) ||
                renderer is null)
            {
                throw new NotSupportedException("Vulkan renderer could not be created for decode-to-render proof.");
            }

            renderer.BindOutput(CreateOffscreenBinding(outputId));
            snapshot = CreateDecodedSourceSnapshot(canvasId, outputId, sourceLease);
            leaseTransferredToSnapshot = true;
            submission = renderer.Submit(snapshot);
            await submission.WaitForCompletionAsync(ProofTimeout, cancellationToken).ConfigureAwait(false);

            var outputFrames = submission.AcquireOutputFrames();
            try
            {
                if (outputFrames.Frames.Count != 1)
                    throw new InvalidOperationException("Decode-to-render proof did not produce exactly one output frame.");
            }
            finally
            {
                outputFrames.DisposeSurfaces();
            }

            submission.DisposeCompleted();
            submission = null;
            snapshot.Dispose();
            snapshot = null;
        }
        catch (Exception ex)
        {
            operationFailure = ex;
        }

        List<Exception>? cleanupErrors = null;
        if (!leaseTransferredToSnapshot)
            TryCleanup(sourceLease.Dispose, ref cleanupErrors);

        if (submission is not null)
        {
            try
            {
                await submission.WaitForCompletionAsync(ProofTimeout, CancellationToken.None).ConfigureAwait(false);
                submission.DisposeCompleted();
            }
            catch (Exception ex)
            {
                (cleanupErrors ??= []).Add(ex);
            }
        }

        TryCleanup(() => snapshot?.Dispose(), ref cleanupErrors);
        TryCleanup(() => renderer?.Dispose(), ref cleanupErrors);
        TryCleanup(guard.Clear, ref cleanupErrors);

        if (operationFailure is not null)
        {
            if (cleanupErrors is not null)
            {
                throw new AggregateException(
                    "Source-frame-to-render proof failed and GPU cleanup also failed.",
                    [operationFailure, .. cleanupErrors]);
            }

            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        if (cleanupErrors is not null)
            throw new AggregateException("Source-frame-to-render proof GPU cleanup failed.", cleanupErrors);
    }

    private static void TryCleanup(Action action, ref List<Exception>? errors)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
    }

    private static async ValueTask<GpuFrameLease> WaitForProviderFrameAsync(
        IVideoFrameProvider provider,
        CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetTimestamp() +
                       TimeProvider.System.TimestampFrequency * (long)ProofTimeout.TotalSeconds;
        while (TimeProvider.System.GetTimestamp() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (provider.TryAcquireLatestFrame(out var lease))
                return lease;

            if (provider.State == MediaSourceState.Failed)
            {
                throw new NotSupportedException(
                    "Windows video file provider failed before publishing a GPU source frame.",
                    provider.LastError);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Windows video file provider did not publish a GPU frame before the proof timeout.");
    }

    private static MediaForgeSourceDefinition CreateVideoFileSourceDefinition(
        SourceId sourceId,
        string path) =>
        new()
        {
            Id = sourceId,
            Name = "MP4 input product proof",
            TypeId = MediaSourceTypes.VideoFile,
            Settings = MediaSourceSettingsSerializer.ToJson(new VideoFileSourceSettings
            {
                Path = path,
                Loop = false
            })
        };

    private static MediaForgeSourceDefinition CreateWebcamSourceDefinition(
        SourceId sourceId,
        WindowsWebcamDeviceInfo device) =>
        new()
        {
            Id = sourceId,
            Name = device.FriendlyName,
            TypeId = MediaSourceTypes.Webcam,
            Settings = MediaSourceSettingsSerializer.ToJson(new WebcamSourceSettings
            {
                DeviceId = device.DeviceId,
                PreferredWidth = 1280,
                PreferredHeight = 720,
                PreferredFrameRate = 30
            })
        };

    private static MediaForgeSourceDefinition CreateWindowCaptureSourceDefinition(
        SourceId sourceId,
        nint windowHandle) =>
        new()
        {
            Id = sourceId,
            Name = "Window capture product proof",
            TypeId = MediaSourceTypes.WindowCapture,
            Settings = MediaSourceSettingsSerializer.ToJson(new WindowCaptureSourceSettings
            {
                WindowHandle = windowHandle,
                CaptureCursor = false
            })
        };

    private static RenderOutputBindingSnapshot CreateOffscreenBinding(RenderOutputId outputId) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            SurfaceSize = new FrameSize(
                (uint)WindowsRenderedOutputH264ProofPipeline.Width,
                (uint)WindowsRenderedOutputH264ProofPipeline.Height),
            BindingVersion = 1
        };

    private static RenderFrameSnapshot CreateDecodedSourceSnapshot(
        CanvasId canvasId,
        RenderOutputId outputId,
        GpuFrameLease sourceLease)
    {
        var size = new FrameSize(
            (uint)WindowsRenderedOutputH264ProofPipeline.Width,
            (uint)WindowsRenderedOutputH264ProofPipeline.Height);
        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Context = new RenderFrameContext(
                1,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1d / WindowsRenderedOutputH264ProofPipeline.FramesPerSecond),
                WindowsRenderedOutputH264ProofPipeline.FramesPerSecond,
                CancellationToken.None),
            FrameLeases = [sourceLease],
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Decode-to-render proof canvas",
                    Size = size,
                    BackgroundColor = ColorRgba.Black,
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Decoded MP4 frame",
                            SourceId = sourceLease.Frame.SourceId,
                            BoundFrame = sourceLease.Frame,
                            LayoutMode = LayoutMode.Fit,
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(size.Width, size.Height)
                            }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Decode-to-render proof output",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = size,
                    CanvasLayoutMode = LayoutMode.Fit,
                    LetterboxColor = ColorRgba.Black
                }
            ]
        };
    }
}

internal sealed class WindowsProductMp4ProofAsset : IAsyncDisposable
{
    private WindowsProductMp4ProofAsset(string path, int width, int height)
    {
        Path = path;
        Width = width;
        Height = height;
    }

    public string Path { get; }

    public int Width { get; }

    public int Height { get; }

    public static async ValueTask<WindowsProductMp4ProofAsset> CreateAsync(CancellationToken cancellationToken)
    {
        var renderEncode = await WindowsRenderedOutputH264ProofPipeline
            .RunSustainedCachedAsync(cancellationToken)
            .ConfigureAwait(false);
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"wtk_mediaforge_decode_proof_asset_{Guid.NewGuid():N}.mp4");

        try
        {
            WindowsMediaFoundationMp4PacketWriter.Write(
                outputPath,
                renderEncode.Packets,
                new FrameSize(
                    (uint)renderEncode.EncoderSettings.Width,
                    (uint)renderEncode.EncoderSettings.Height),
                renderEncode.EncoderSettings.FramesPerSecond);

            if (!IsoBmffMp4Writer.HasExperimentalBoxStructure(outputPath))
            {
                throw new NotSupportedException("Generated decode proof asset failed basic MP4 materialization validation.");
            }

            return new WindowsProductMp4ProofAsset(
                outputPath,
                renderEncode.EncoderSettings.Width,
                renderEncode.EncoderSettings.Height);
        }
        catch
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(Path))
            File.Delete(Path);

        return ValueTask.CompletedTask;
    }
}
