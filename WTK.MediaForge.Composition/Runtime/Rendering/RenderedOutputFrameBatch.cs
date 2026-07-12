using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderedOutputFrameBatch
{
    internal static readonly TimeSpan DefaultSurfaceDisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _allLeasesReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<ValueTask>? _leaseReleased;
    private int _leaseCount;

    public RenderedOutputFrameBatch(
        IReadOnlyList<RenderedOutputFrame> frames,
        Func<ValueTask>? leaseReleased = null)
        : this(
            frames,
            CreateFrameExecutionContext(
                RenderFrameSnapshotFactory.CreateDefaultContext(),
                (frames ?? throw new ArgumentNullException(nameof(frames)))
                    .Select(static frame => frame.OutputId)
                    .ToArray()),
            leaseReleased)
    {
    }

    public RenderedOutputFrameBatch(
        IReadOnlyList<RenderedOutputFrame> frames,
        FrameExecutionContext frameContext,
        Func<ValueTask>? leaseReleased = null)
    {
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        FrameContext = frameContext ?? throw new ArgumentNullException(nameof(frameContext));
        _leaseReleased = leaseReleased;
        if (frames.Count == 0)
            _allLeasesReleased.TrySetResult();
    }

    public IReadOnlyList<RenderedOutputFrame> Frames { get; }

    public FrameExecutionContext FrameContext { get; }

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

        return new RenderedOutputFrameBatch(
            frames,
            CreateFrameExecutionContext(
                snapshot.Context,
                frames.Select(static frame => frame.OutputId).ToArray()));
    }

    public static RenderedOutputFrameBatch FromRenderedSurfaces(
        IReadOnlyList<IRenderedOutputSurfaceLease> surfaces,
        RenderFrameContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        var frames = surfaces
            .Select(surface => new RenderedOutputFrame(
                surface.OutputId,
                surface.Size,
                surface.Format,
                surface.BackendKind,
                surface))
            .ToArray();

        var renderContext = context ?? RenderFrameSnapshotFactory.CreateDefaultContext();
        return new RenderedOutputFrameBatch(
            frames,
            CreateFrameExecutionContext(
                renderContext,
                frames.Select(static frame => frame.OutputId).ToArray()));
    }

    public RenderOutputFrameLease CreateLease(
        RenderedOutputFrame frame,
        RenderOutputFrameInfo info)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(info);

        Interlocked.Increment(ref _leaseCount);
        return frame.CreateLease(info, ReleaseLeaseAsync);
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

    public void DisposeSurfaces() => DisposeSurfaces(DefaultSurfaceDisposeTimeout);

    public void DisposeSurfaces(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Surface dispose timeout must be positive.");

        List<Exception>? errors = null;

        foreach (var frame in Frames)
        {
            try
            {
                frame.DisposeSurfaceAsync()
                    .AsTask()
                    .WaitAsync(timeout)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (TimeoutException ex)
            {
                (errors ??= []).Add(new TimeoutException(
                    $"Rendered output surface for output {frame.OutputId} did not dispose within {timeout}.",
                    ex));
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose rendered output surfaces.", errors);
    }

    private async ValueTask ReleaseLeaseAsync()
    {
        var remaining = Interlocked.Decrement(ref _leaseCount);
        if (remaining < 0)
            throw new InvalidOperationException("Rendered output frame lease was released more times than it was acquired.");

        Exception? releaseError = null;
        if (_leaseReleased is not null)
        {
            try
            {
                await _leaseReleased().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                releaseError = ex;
            }
        }

        if (remaining == 0)
            _allLeasesReleased.TrySetResult();

        if (releaseError is not null)
            throw releaseError;
    }

    private static FrameExecutionContext CreateFrameExecutionContext(
        RenderFrameContext context,
        IReadOnlyList<RenderOutputId> targetOutputs) =>
        new()
        {
            FrameId = context.FrameNumber,
            PresentationTime = context.PresentationTime,
            FrameBudget = context.DeltaTime,
            TargetOutputs = targetOutputs
        };
}
