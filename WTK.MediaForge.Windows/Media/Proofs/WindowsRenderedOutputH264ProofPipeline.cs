using System.Collections.Immutable;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Interop;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal sealed record WindowsRenderedOutputH264ProofResult(
    EncodedVideoPacket Packet,
    IReadOnlyList<EncodedVideoPacket> Packets,
    IReadOnlyList<MediaTransportAuditEvent> AuditEvents,
    HardwareVideoEncoderSettings EncoderSettings,
    int RenderedFrameCount);

internal sealed record WindowsRenderedOutputH264SustainedProofOptions(
    int FrameCount,
    int MinimumPacketCount,
    TimeSpan Timeout)
{
    public static WindowsRenderedOutputH264SustainedProofOptions Default { get; } =
        new(60, 2, TimeSpan.FromSeconds(15));
}

internal static class WindowsRenderedOutputH264ProofPipeline
{
    public const int Width = 320;
    public const int Height = 180;
    public const int FramesPerSecond = 60;
    private const int MaxRenderedFrames = 16;
    private static readonly TimeSpan ProofTimeout = TimeSpan.FromSeconds(5);
    private static readonly object CacheGate = new();
    private static Task<WindowsRenderedOutputH264ProofResult>? cachedProofTask;
    private static Task<WindowsRenderedOutputH264ProofResult>? cachedSustainedProofTask;

    public static async ValueTask<WindowsRenderedOutputH264ProofResult> RunCachedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<WindowsRenderedOutputH264ProofResult> task;
        lock (CacheGate)
        {
            cachedProofTask ??= RunAsync(CancellationToken.None).AsTask();
            task = cachedProofTask;
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                lock (CacheGate)
                {
                    if (ReferenceEquals(cachedProofTask, task))
                        cachedProofTask = null;
                }
            }

            throw;
        }
    }

    public static async ValueTask<WindowsRenderedOutputH264ProofResult> RunAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunFramesAsync(
                maxRenderedFrames: MaxRenderedFrames,
                minimumPacketCount: 1,
                stopAfterMinimumPackets: true,
                ProofTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public static ValueTask<WindowsRenderedOutputH264ProofResult> RunSustainedAsync(
        CancellationToken cancellationToken) =>
        RunSustainedCachedAsync(cancellationToken);

    public static async ValueTask<WindowsRenderedOutputH264ProofResult> RunSustainedCachedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<WindowsRenderedOutputH264ProofResult> task;
        lock (CacheGate)
        {
            cachedSustainedProofTask ??= RunSustainedAsync(
                WindowsRenderedOutputH264SustainedProofOptions.Default,
                CancellationToken.None).AsTask();
            task = cachedSustainedProofTask;
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                lock (CacheGate)
                {
                    if (ReferenceEquals(cachedSustainedProofTask, task))
                        cachedSustainedProofTask = null;
                }
            }

            throw;
        }
    }

    public static async ValueTask<WindowsRenderedOutputH264ProofResult> RunSustainedAsync(
        WindowsRenderedOutputH264SustainedProofOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FrameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Sustained proof frame count must be positive.");

        if (options.MinimumPacketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Sustained proof minimum packet count must be positive.");

        if (options.MinimumPacketCount > options.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(options), "Sustained proof cannot require more packets than submitted frames.");

        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Sustained proof timeout must be positive.");

        return await RunFramesAsync(
                options.FrameCount,
                options.MinimumPacketCount,
                stopAfterMinimumPackets: false,
                options.Timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<WindowsRenderedOutputH264ProofResult> RunFramesAsync(
        int maxRenderedFrames,
        int minimumPacketCount,
        bool stopAfterMinimumPackets,
        TimeSpan perSubmissionTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows rendered-output H.264 proof requires Windows.");

        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var guard = new RenderThreadGuard();
        guard.BindToCurrentThread();

        MediaForgeVulkanRenderer? renderer = null;
        IRenderFrameSubmission? submission = null;
        try
        {
            if (!MediaForgeVulkanRenderer.TryCreate(
                    guard,
                    diagnostics: null,
                    NullVulkanRendererFaultInjector.Instance,
                    out renderer) ||
                renderer is null)
            {
                throw new NotSupportedException("Vulkan renderer could not be created for render-to-encode proof.");
            }

            renderer.BindOutput(CreateOffscreenBinding(outputId));

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            using var ownedDevice = OwnedD3D11EncoderDevice.Create(adapter);

            var settings = new HardwareVideoEncoderSettings
            {
                Width = Width,
                Height = Height,
                FramesPerSecond = FramesPerSecond,
                BitrateBitsPerSecond = 2_000_000,
                KeyFrameIntervalFrames = FramesPerSecond,
                PixelFormat = "NV12"
            };
            var audit = new CollectingMediaTransportAuditSink();
            await using var encoder = new MediaFoundationHardwareVideoEncoder(
                ownedDevice.Device,
                settings);
            var frameAdapter = new RenderedOutputEncodeFrameAdapter(
                new RenderedOutputEncoderInputPreparer(
                    new WindowsRenderedOutputEncoderSurfaceExporter(ownedDevice.Device),
                    new WindowsRenderedOutputEncoderInputConverter(ownedDevice.Device)));

            var packets = new List<EncodedVideoPacket>();
            for (var frameIndex = 0; frameIndex < maxRenderedFrames; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var snapshot = CreateSolidSnapshot(
                    canvasId,
                    outputId,
                    frameIndex + 1);

                submission = renderer.Submit(snapshot);
                await submission.WaitForCompletionAsync(perSubmissionTimeout, cancellationToken).ConfigureAwait(false);

                var outputFrames = submission.AcquireOutputFrames();
                try
                {
                    var frame = outputFrames.Frames.Single();
                    using var scheduled = await frameAdapter
                        .CreateScheduledFrameAsync(
                            frame,
                            outputFrames.FrameContext,
                            encoder.InputRequirement,
                            audit,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (scheduled.EncoderInputLease is null)
                        throw new InvalidOperationException("Render-to-encode proof did not create an encoder input lease.");

                    var packet = await encoder
                        .EncodeAsync(
                            new EncodeFrameContext
                            {
                                InputLease = scheduled.EncoderInputLease,
                                FrameNumber = frameIndex + 1,
                                PresentationTime = TimeSpan.FromSeconds(frameIndex / (double)FramesPerSecond),
                                CancellationToken = cancellationToken
                            },
                            audit)
                        .ConfigureAwait(false);

                    if (packet is not null)
                    {
                        ValidatePacket(packet);
                        packets.Add(packet);
                        if (stopAfterMinimumPackets && packets.Count >= minimumPacketCount)
                        {
                            return new WindowsRenderedOutputH264ProofResult(
                                packets[0],
                                packets.ToArray(),
                                audit.Events.ToArray(),
                                settings,
                                frameIndex + 1);
                        }
                    }
                }
                finally
                {
                    outputFrames.DisposeSurfaces();
                    submission.DisposeCompleted();
                    submission = null;
                }
            }

            if (packets.Count >= minimumPacketCount)
            {
                return new WindowsRenderedOutputH264ProofResult(
                    packets[0],
                    packets.ToArray(),
                    audit.Events.ToArray(),
                    settings,
                    maxRenderedFrames);
            }

            throw new NotSupportedException(
                $"Media Foundation hardware encoder accepted rendered output but emitted {packets.Count} packet(s) after {maxRenderedFrames} rendered frame(s); required {minimumPacketCount}.");
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

            renderer?.Dispose();
            guard.Clear();
        }
    }

    private static void ValidatePacket(EncodedVideoPacket packet)
    {
        if (packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
            throw new NotSupportedException("Render-to-encode proof requires BackendOutputValidated packet evidence.");

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException("Render-to-encode proof requires H.264 packets.");

        if (packet.Data.IsEmpty)
            throw new InvalidOperationException("Render-to-encode proof produced an empty encoded packet.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("Render-to-encode proof requires explicit H.264 bitstream format.");
    }

    private static RenderOutputBindingSnapshot CreateOffscreenBinding(RenderOutputId outputId) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            SurfaceSize = new FrameSize(Width, Height),
            BindingVersion = 1
        };

    private static RenderFrameSnapshot CreateSolidSnapshot(
        CanvasId canvasId,
        RenderOutputId outputId,
        long frameNumber)
    {
        var size = new FrameSize(Width, Height);
        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Context = new RenderFrameContext(
                frameNumber,
                TimeSpan.FromSeconds((frameNumber - 1) / (double)FramesPerSecond),
                TimeSpan.FromSeconds(1d / FramesPerSecond),
                FramesPerSecond,
                CancellationToken.None),
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Render-to-encode proof canvas",
                    Size = size,
                    BackgroundColor = new ColorRgba(0.05f, 0.08f, 0.12f, 1f),
                    Objects =
                    [
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Proof Fill",
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(Width, Height)
                            },
                            FillColor = new ColorRgba(0.08f, 0.55f, 0.95f, 1f)
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Proof Offscreen",
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
