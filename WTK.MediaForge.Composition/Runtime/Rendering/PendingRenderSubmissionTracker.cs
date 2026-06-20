using System.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public class PendingRenderSubmissionTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IRenderFrameSubmission> _pending = [];
    private bool _disposed;

    public PendingRenderSubmissionTracker(int maxFramesInFlight = 2)
    {
        if (maxFramesInFlight < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFramesInFlight));

        MaxFramesInFlight = maxFramesInFlight;
    }

    public int MaxFramesInFlight { get; }

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    public bool CanAcceptFrame
    {
        get
        {
            lock (_gate)
                return !_disposed && _pending.Count < MaxFramesInFlight;
        }
    }

    public virtual void Add(IRenderFrameSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pending.Count >= MaxFramesInFlight)
                throw new InvalidOperationException("Max frames in flight exceeded.");

            _pending.Add(submission);
        }
    }

    public void PollCompleted()
    {
        List<IRenderFrameSubmission> completed;

        lock (_gate)
        {
            completed = [];

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].IsCompleted)
                {
                    completed.Add(_pending[i]);
                    _pending.RemoveAt(i);
                }
            }
        }

        foreach (var submission in completed)
            submission.DisposeCompleted();
    }

    public async ValueTask ShutdownAsync(
        IRenderBackend backend,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backend);

        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        PollCompleted();

        await backend
            .WaitIdleAsync(GetRemainingTime(deadline), cancellationToken)
            .ConfigureAwait(false);

        PollCompleted();

        List<IRenderFrameSubmission> remaining;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            remaining = [.. _pending];
            _pending.Clear();
        }

        foreach (var submission in remaining)
        {
            await submission
                .WaitForCompletionAsync(GetRemainingTime(deadline), cancellationToken)
                .ConfigureAwait(false);
            submission.DisposeCompleted();
        }
    }

    public void Dispose()
    {
        PollCompleted();

        List<IRenderFrameSubmission> remaining;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            remaining = [.. _pending];
            _pending.Clear();
        }

        foreach (var submission in remaining)
        {
            if (!submission.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Cannot dispose tracker with incomplete submissions. Use ShutdownAsync instead.");
            }

            submission.DisposeCompleted();
        }
    }

    private static TimeSpan GetRemainingTime(long deadline)
    {
        var remainingTicks = deadline - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }
}
