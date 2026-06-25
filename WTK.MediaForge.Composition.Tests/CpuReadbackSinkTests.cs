using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class CpuReadbackSinkTests
{
    [Fact]
    public async Task CpuReadbackSink_receives_pixel_buffer()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeCpuReadableSurface(outputId, new FrameSize(2, 2));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        CpuReadbackFrame? received = null;
        var sink = new CpuReadbackSink(onFrame: (readback, _) =>
        {
            received = readback;
            return ValueTask.CompletedTask;
        });

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);
        await sink.OnFrameAsync(
            batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
            CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(outputId, received.OutputId);
        Assert.Equal(2u, received.Size.Width);
        Assert.Equal(2u, received.Size.Height);
        Assert.Equal(RenderPixelFormat.Rgba8Unorm, received.Format);
        Assert.Equal([255, 0, 0, 255], received.Pixels.Slice(0, 4).ToArray());
    }

    [Fact]
    public async Task CpuReadbackSink_stride_and_format_are_valid()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeCpuReadableSurface(outputId, new FrameSize(3, 2));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        CpuReadbackFrame? received = null;
        var sink = new CpuReadbackSink(onFrame: (readback, _) =>
        {
            received = readback;
            return ValueTask.CompletedTask;
        });

        await sink.StartAsync(CreateContext(outputId, new FrameSize(3, 2)), CancellationToken.None);
        await sink.OnFrameAsync(
            batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
            CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(12, received.StrideBytes);
        Assert.Equal(24, received.Pixels.Length);
        Assert.Equal(RenderPixelFormat.Rgba8Unorm, received.Format);
        Assert.Equal(RenderBackendKind.Vulkan, received.BackendKind);
    }

    [Fact]
    public async Task CpuReadbackSink_releases_output_frame_lease_after_readback()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeCpuReadableSurface(outputId, new FrameSize(2, 2));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new CpuReadbackSink(onFrame: (_, _) =>
        {
            Assert.False(batch.HasOutstandingLeases);
            Assert.Equal(1, surface.DisposeCount);
            return ValueTask.CompletedTask;
        });

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);
        await sink.OnFrameAsync(
            batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
            CancellationToken.None);

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task CpuReadbackSink_requires_started_state()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeCpuReadableSurface(outputId, new FrameSize(2, 2));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new CpuReadbackSink();
        var lease = batch.CreateLease(frame, CreateInfo(frame, sink.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.OnFrameAsync(lease, CancellationToken.None).AsTask());

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task CpuReadbackSink_rate_limit_skips_excess_frames()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeCpuReadableSurface(outputId, new FrameSize(2, 2));
        var readCount = 0;
        var sink = new CpuReadbackSink(
            maxFramesPerSecond: 1,
            onFrame: (_, _) =>
            {
                Interlocked.Increment(ref readCount);
                return ValueTask.CompletedTask;
            });

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
            var frame = Assert.Single(batch.Frames);
            await sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id, i + 1)),
                CancellationToken.None);
        }

        Assert.Equal(1, readCount);
        Assert.Equal(3, surface.DisposeCount);
    }

    private static RenderOutputSinkContext CreateContext(
        RenderOutputId outputId,
        FrameSize? size = null) =>
        new(
            outputId,
            size ?? new FrameSize(2, 2),
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Vulkan);

    private static RenderOutputFrameInfo CreateInfo(
        RenderedOutputFrame frame,
        RenderOutputSinkId sinkId,
        long frameNumber = 7) =>
        new(
            frame.OutputId,
            sinkId,
            frameNumber,
            TimeSpan.FromMilliseconds(33),
            frame.Size,
            frame.Format,
            frame.BackendKind);

    private sealed class FakeCpuReadableSurface(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease, ICpuReadableRenderedOutputSurfaceLease
    {
        private int _disposeCount;

        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = new object();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask<CpuReadbackFrame> ReadCpuFrameAsync(
            RenderOutputFrameInfo info,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stride = checked((int)Size.Width * 4);
            var pixels = new byte[checked(stride * (int)Size.Height)];
            pixels[0] = 255;
            pixels[3] = 255;

            return ValueTask.FromResult(new CpuReadbackFrame(info, stride, pixels));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
