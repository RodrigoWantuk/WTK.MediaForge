using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputSinkQueueTests
{
    [Fact]
    public void KeepLatest_replaces_pending_frame_and_releases_old()
    {
        var released = new List<long>();
        var queue = CreateQueue(
            RenderOutputSinkBackpressureMode.KeepLatest,
            lease => released.Add(lease.FrameNumber));
        var first = CreateLease(1);
        var second = CreateLease(2);

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));

        Assert.Equal([1], released);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropOldest_releases_oldest_frame()
    {
        var released = new List<long>();
        var queue = CreateQueue(
            RenderOutputSinkBackpressureMode.DropOldest,
            lease => released.Add(lease.FrameNumber));

        Assert.True(queue.TryEnqueue(CreateLease(1)));
        Assert.True(queue.TryEnqueue(CreateLease(2)));

        Assert.Equal([1], released);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropNewest_releases_new_frame()
    {
        var released = new List<long>();
        var queue = CreateQueue(
            RenderOutputSinkBackpressureMode.DropNewest,
            lease => released.Add(lease.FrameNumber));

        Assert.True(queue.TryEnqueue(CreateLease(1)));
        Assert.False(queue.TryEnqueue(CreateLease(2)));

        Assert.Equal([2], released);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(1, dequeued.FrameNumber);
    }

    [Fact]
    public void StopAccepting_releases_new_frame()
    {
        var released = new List<long>();
        var queue = CreateQueue(
            RenderOutputSinkBackpressureMode.KeepLatest,
            lease => released.Add(lease.FrameNumber));

        queue.StopAccepting();

        Assert.False(queue.TryEnqueue(CreateLease(5)));
        Assert.Equal([5], released);
    }

    private static RenderOutputSinkQueue CreateQueue(
        RenderOutputSinkBackpressureMode mode,
        Action<RenderOutputFrameLease> onDrop) =>
        new(
            capacity: 1,
            backpressureMode: mode,
            lease =>
            {
                onDrop(lease);
                lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            });

    private static RenderOutputFrameLease CreateLease(long frameNumber)
    {
        var frameSize = new FrameSize(1280, 720);
        return new RenderOutputFrameLease(
            new RenderOutputFrameInfo(
                RenderOutputId.New(),
                RenderOutputSinkId.New(),
                frameNumber,
                TimeSpan.Zero,
                frameSize,
                RenderPixelFormat.Rgba8Unorm,
                RenderBackendKind.Vulkan));
    }
}
