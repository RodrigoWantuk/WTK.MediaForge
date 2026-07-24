using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputSinkQueueTests
{
    [Fact]
    public void KeepLatest_returns_replaced_lease_to_release()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);
        var first = CreateLease(1);
        var second = CreateLease(2);

        Assert.Equal(SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue, queue.TryEnqueue(first.Lease).Kind);
        var replacement = queue.TryEnqueue(second.Lease);

        Assert.Equal(SinkQueueEnqueueResultKind.ReplacedOldest, replacement.Kind);
        Assert.Same(first.Lease, replacement.LeaseToRelease);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropOldest_returns_oldest_lease_to_release()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropOldest);

        Assert.Equal(
            SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue,
            queue.TryEnqueue(CreateLease(1).Lease).Kind);
        var replacement = queue.TryEnqueue(CreateLease(2).Lease);

        Assert.Equal(SinkQueueEnqueueResultKind.ReplacedOldest, replacement.Kind);
        Assert.Equal(1, replacement.LeaseToRelease!.FrameNumber);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropNewest_returns_incoming_lease_to_release()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropNewest);

        Assert.Equal(
            SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue,
            queue.TryEnqueue(CreateLease(1).Lease).Kind);
        var dropped = queue.TryEnqueue(CreateLease(2).Lease);

        Assert.Equal(SinkQueueEnqueueResultKind.DroppedIncoming, dropped.Kind);
        Assert.Equal(2, dropped.LeaseToRelease!.FrameNumber);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(1, dequeued.FrameNumber);
    }

    [Fact]
    public void Rejected_returns_incoming_lease_to_release()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);
        queue.StopAccepting();

        var rejected = queue.TryEnqueue(CreateLease(5).Lease);

        Assert.Equal(SinkQueueEnqueueResultKind.Rejected, rejected.Kind);
        Assert.Equal(5, rejected.LeaseToRelease!.FrameNumber);
    }

    [Fact]
    public void Enqueue_result_distinguishes_empty_from_non_empty_queue()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);

        var first = queue.TryEnqueue(CreateLease(1).Lease);
        var second = queue.TryEnqueue(CreateLease(2).Lease);

        Assert.Equal(SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue, first.Kind);
        Assert.Equal(SinkQueueEnqueueResultKind.ReplacedOldest, second.Kind);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Queue_never_disposes_returned_leases()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropNewest);
        var (incoming, releaseCount) = CreateLease(2);

        queue.TryEnqueue(CreateLease(1).Lease);
        var dropped = queue.TryEnqueue(incoming);

        Assert.Same(incoming, dropped.LeaseToRelease);
        Assert.Equal(0, releaseCount());
    }

    private static RenderOutputSinkQueue CreateQueue(RenderOutputSinkBackpressureMode mode) =>
        new(capacity: 1, backpressureMode: mode);

    private static (RenderOutputFrameLease Lease, Func<int> ReleaseCount) CreateLease(long frameNumber)
    {
        var releaseCount = 0;
        var lease = new RenderOutputFrameLease(
            new RenderOutputFrameInfo(
                RenderOutputId.New(),
                RenderOutputSinkId.New(),
                frameNumber,
                TimeSpan.Zero,
                new FrameSize(1280, 720),
                RenderPixelFormat.Rgba8Unorm,
                RenderBackendKind.Vulkan),
            release: () =>
            {
                Interlocked.Increment(ref releaseCount);
                return ValueTask.CompletedTask;
            });

        return (lease, () => Volatile.Read(ref releaseCount));
    }
}
