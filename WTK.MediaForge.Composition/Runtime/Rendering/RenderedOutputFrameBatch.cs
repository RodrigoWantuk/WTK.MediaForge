using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderedOutputFrameBatch
{
    private readonly TaskCompletionSource _allLeasesReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _leaseCount;

    public RenderedOutputFrameBatch(IReadOnlyList<RenderedOutputFrame> frames)
    {
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        if (frames.Count == 0)
            _allLeasesReleased.TrySetResult();
    }

    public IReadOnlyList<RenderedOutputFrame> Frames { get; }

    public bool HasOutstandingLeases => Volatile.Read(ref _leaseCount) > 0;

    public static RenderedOutputFrameBatch FromSnapshot(
        RenderFrameSnapshot snapshot,
        RenderPixelFormat format = RenderPixelFormat.Rgba8Unorm,
        RenderBackendKind backendKind = RenderBackendKind.Vulkan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var frames = snapshot.Outputs
            .Select(output => new RenderedOutputFrame(
                output.Id,
                output.OutputSize,
                format,
                backendKind))
            .ToArray();

        return new RenderedOutputFrameBatch(frames);
    }

    public RenderOutputFrameLease CreateLease(
        RenderedOutputFrame frame,
        RenderOutputFrameInfo info)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(info);

        Interlocked.Increment(ref _leaseCount);
        return new RenderOutputFrameLease(info, ReleaseLeaseAsync);
    }

    public async ValueTask WaitForLeasesReleasedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!HasOutstandingLeases)
            return;

        await _allLeasesReleased.Task
            .WaitAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private ValueTask ReleaseLeaseAsync()
    {
        var remaining = Interlocked.Decrement(ref _leaseCount);
        if (remaining < 0)
            throw new InvalidOperationException("Rendered output frame lease was released more times than it was acquired.");

        if (remaining == 0)
            _allLeasesReleased.TrySetResult();

        return ValueTask.CompletedTask;
    }
}
