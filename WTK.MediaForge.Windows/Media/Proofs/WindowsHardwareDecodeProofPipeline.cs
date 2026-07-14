using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
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

        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var sourceId = SourceId.New();
        var guard = new RenderThreadGuard();
        guard.BindToCurrentThread();

        MediaForgeVulkanRenderer? renderer = null;
        IRenderFrameSubmission? submission = null;
        RenderFrameSnapshot? snapshot = null;
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
            var sourceLease = DecodedFrameToSourceFrameAdapter.Instance
                .CreateSourceFrameLease(decoded, sourceId, frameNumber: 1);
            snapshot = CreateDecodedSourceSnapshot(canvasId, outputId, sourceLease);
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
        finally
        {
            if (submission is not null)
            {
                try
                {
                    await submission.WaitForCompletionAsync(ProofTimeout, CancellationToken.None).ConfigureAwait(false);
                    submission.DisposeCompleted();
                }
                catch
                {
                    // The original proof failure is more actionable than best-effort cleanup failure here.
                }
            }

            snapshot?.Dispose();
            renderer?.Dispose();
            guard.Clear();
        }
    }

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
            .RunAsync(cancellationToken)
            .ConfigureAwait(false);
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"wtk_mediaforge_decode_proof_asset_{Guid.NewGuid():N}.mp4");

        try
        {
            await using var sink = new RecordingMp4PacketSink(outputPath);
            await sink
                .StartAsync(
                    new EncodedPacketSinkContext
                    {
                        Codec = EncodedVideoCodec.H264,
                        Size = new FrameSize(
                            (uint)renderEncode.EncoderSettings.Width,
                            (uint)renderEncode.EncoderSettings.Height),
                        FramesPerSecond = renderEncode.EncoderSettings.FramesPerSecond
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await sink.WritePacketAsync(renderEncode.Packet, cancellationToken).ConfigureAwait(false);
            await sink.StopAsync(cancellationToken).ConfigureAwait(false);

            if (!IsoBmffMp4Writer.HasValidH264BoxStructure(
                    outputPath,
                    new IsoBmffMp4Writer.TrackMetadata(
                        (uint)renderEncode.EncoderSettings.Width,
                        (uint)renderEncode.EncoderSettings.Height),
                    minimumSampleCount: 1))
            {
                throw new NotSupportedException("Generated decode proof asset failed MP4/H.264 box validation.");
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
