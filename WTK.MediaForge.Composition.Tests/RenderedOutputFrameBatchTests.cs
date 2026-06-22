using System.Collections.Immutable;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderedOutputFrameBatchTests
{
    [Fact]
    public async Task RenderedOutputFrameLease_double_dispose_is_idempotent()
    {
        var outputId = RenderOutputId.New();
        var surface = new TrackingRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var releases = 0;
        var lease = frame.CreateLease(CreateInfo(frame), () =>
        {
            Interlocked.Increment(ref releases);
            return ValueTask.CompletedTask;
        });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(1, Volatile.Read(ref releases));
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task RenderedOutputFrame_surface_disposed_after_last_sink_lease()
    {
        var outputId = RenderOutputId.New();
        var surface = new TrackingRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);

        var first = batch.CreateLease(frame, CreateInfo(frame, RenderOutputSinkId.New()));
        var second = batch.CreateLease(frame, CreateInfo(frame, RenderOutputSinkId.New()));

        await first.DisposeAsync();
        Assert.True(batch.HasOutstandingLeases);
        Assert.Equal(0, surface.DisposeCount);

        await second.DisposeAsync();
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public void Null_backend_may_use_snapshot_frames_for_tests_only()
    {
        var outputId = RenderOutputId.New();
        var snapshot = new RenderFrameSnapshot
        {
            Outputs = ImmutableArray.Create(new RenderOutputStateSnapshot
            {
                Id = outputId,
                OutputSize = new FrameSize(320, 180)
            })
        };

        var batch = RenderedOutputFrameBatch.FromSnapshot(snapshot);
        var frame = Assert.Single(batch.Frames);

        Assert.IsType<NullRenderedOutputSurfaceLease>(frame.SurfaceLease);
        Assert.Null(frame.SurfaceLease.BackendSurface);
    }

    private static RenderOutputFrameInfo CreateInfo(
        RenderedOutputFrame frame,
        RenderOutputSinkId? sinkId = null) =>
        new(
            frame.OutputId,
            sinkId ?? RenderOutputSinkId.New(),
            frameNumber: 1,
            timestamp: TimeSpan.Zero,
            frame.Size,
            frame.Format,
            frame.BackendKind);

    private sealed class TrackingRenderedOutputSurfaceLease(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = new object();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
