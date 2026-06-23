using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputSinkQueueTests
{
    [Fact]
    public void KeepLatest_returns_replaced_old_to_dispatcher()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);
        var first = CreateLease(1);
        var second = CreateLease(2);

        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled,
            queue.TryEnqueue(first.Lease, out var firstRelease));
        Assert.Null(firstRelease);

        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.ReplacedPendingOldReturnedToCaller,
            queue.TryEnqueue(second.Lease, out var secondRelease));
        Assert.Same(first.Lease, secondRelease);

        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropOldest_returns_dropped_oldest_to_dispatcher()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropOldest);

        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled,
            queue.TryEnqueue(CreateLease(1).Lease, out _));
        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.ReplacedPendingOldReturnedToCaller,
            queue.TryEnqueue(CreateLease(2).Lease, out var release));
        Assert.Equal(1, release!.FrameNumber);

        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(2, dequeued.FrameNumber);
    }

    [Fact]
    public void DropNewest_returns_rejected_and_dispatcher_releases_incoming()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropNewest);

        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled,
            queue.TryEnqueue(CreateLease(1).Lease, out _));
        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.DroppedIncomingReturnedToCaller,
            queue.TryEnqueue(CreateLease(2).Lease, out var release));
        Assert.Equal(2, release!.FrameNumber);

        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(1, dequeued.FrameNumber);
    }

    [Fact]
    public void StopAccepting_returns_rejected_and_dispatcher_releases_incoming()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);
        queue.StopAccepting();

        Assert.Equal(
            RenderOutputSinkQueueEnqueueResult.RejectedCallerMustRelease,
            queue.TryEnqueue(CreateLease(5).Lease, out var release));
        Assert.Equal(5, release!.FrameNumber);
    }

    [Fact]
    public void Enqueue_result_distinguishes_new_items_from_replacements()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.KeepLatest);

        var first = queue.TryEnqueue(CreateLease(1).Lease, out var firstRelease);
        var replacement = queue.TryEnqueue(CreateLease(2).Lease, out var secondRelease);

        Assert.Equal(RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled, first);
        Assert.Equal(RenderOutputSinkQueueEnqueueResult.ReplacedPendingOldReturnedToCaller, replacement);
        Assert.Null(firstRelease);
        Assert.Equal(1, secondRelease!.FrameNumber);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Queue_never_disposes_returned_leases()
    {
        var queue = CreateQueue(RenderOutputSinkBackpressureMode.DropNewest);
        var (incoming, releaseCount) = CreateLease(2);

        queue.TryEnqueue(CreateLease(1).Lease, out _);
        queue.TryEnqueue(incoming, out var release);

        Assert.Same(incoming, release);
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
