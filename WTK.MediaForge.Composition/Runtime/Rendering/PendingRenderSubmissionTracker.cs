using System.Diagnostics;
using System.Runtime.CompilerServices;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Tracks in-flight GPU render submissions without blocking on completion.
/// Shutdown uses <see cref="ShutdownAsync"/> to wait with timeouts before destroying resources.
/// </summary>
internal class PendingRenderSubmissionTracker : IDisposable
{
    private enum PendingTrackerState
    {
        Active,
        ShutdownInProgress,
        Disposed
    }

    private readonly object _gate = new();
    private readonly List<IRenderFrameSubmission> _pending = [];
    private readonly HashSet<IRenderFrameSubmission> _cleanupFailureReported =
        new(SubmissionReferenceEqualityComparer.Instance);
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private PendingTrackerState _state = PendingTrackerState.Active;

    public PendingRenderSubmissionTracker(int maxFramesInFlight = 2, IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        if (maxFramesInFlight < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFramesInFlight));

        MaxFramesInFlight = maxFramesInFlight;
        _diagnostics = diagnostics;
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
                return _state == PendingTrackerState.Active && _pending.Count < MaxFramesInFlight;
        }
    }

    public virtual void Add(IRenderFrameSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        lock (_gate)
        {
            if (_state == PendingTrackerState.Disposed)
                throw new ObjectDisposedException(nameof(PendingRenderSubmissionTracker));

            if (_state == PendingTrackerState.ShutdownInProgress)
            {
                throw new InvalidOperationException(
                    "Cannot add submissions while tracker shutdown is in progress.");
            }

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
            if (_state == PendingTrackerState.Disposed)
                return;

            completed = _pending.Where(static submission => submission.IsCompleted).ToList();
        }

        var disposed = new List<IRenderFrameSubmission>();

        foreach (var submission in completed)
        {
            if (TryDisposeCompleted(submission, reportFailure: true, propagateFailure: false))
                disposed.Add(submission);
        }

        if (disposed.Count == 0)
            return;

        lock (_gate)
        {
            foreach (var submission in disposed)
                _pending.Remove(submission);
        }
    }

    public async ValueTask ShutdownAsync(
        IRenderBackend backend,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backend);

        lock (_gate)
        {
            if (_state == PendingTrackerState.Disposed)
                return;

            if (_state == PendingTrackerState.Active)
                _state = PendingTrackerState.ShutdownInProgress;
        }

        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        PollCompleted();

        await backend
            .WaitIdleAsync(GetRemainingTime(deadline), cancellationToken)
            .ConfigureAwait(false);

        List<IRenderFrameSubmission> remaining;

        lock (_gate)
        {
            if (_state == PendingTrackerState.Disposed)
                return;

            remaining = [.. _pending];
        }

        var errors = new List<Exception>();
        var disposed = new List<IRenderFrameSubmission>();

        foreach (var submission in remaining)
        {
            try
            {
                await submission
                    .WaitForCompletionAsync(GetRemainingTime(deadline), cancellationToken)
                    .ConfigureAwait(false);
                submission.DisposeCompleted();
                disposed.Add(submission);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        lock (_gate)
        {
            foreach (var submission in disposed)
                _pending.Remove(submission);

            if (errors.Count == 0)
            {
                _state = PendingTrackerState.Disposed;
                _pending.Clear();
            }
        }

        if (errors.Count > 0)
            throw new AggregateException("Failed to shut down one or more render submissions.", errors);
    }

    private bool TryDisposeCompleted(
        IRenderFrameSubmission submission,
        bool reportFailure,
        bool propagateFailure)
    {
        try
        {
            submission.DisposeCompleted();
            _cleanupFailureReported.Remove(submission);
            return true;
        }
        catch (Exception ex)
        {
            if (reportFailure && _cleanupFailureReported.Add(submission))
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "render.submission_dispose_failed",
                    "Failed to dispose completed render submission.",
                    nameof(PendingRenderSubmissionTracker),
                    ex);
            }

            if (propagateFailure)
                throw;

            return false;
        }
    }

    public void Dispose()
    {
        PollCompleted();

        List<IRenderFrameSubmission> remaining;

        lock (_gate)
        {
            if (_state == PendingTrackerState.Disposed)
                return;

            _state = PendingTrackerState.ShutdownInProgress;
            remaining = [.. _pending];
        }

        var errors = new List<Exception>();
        var disposed = new List<IRenderFrameSubmission>();

        foreach (var submission in remaining)
        {
            try
            {
                if (!submission.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Cannot dispose tracker with incomplete submissions. Use ShutdownAsync instead.");
                }

                submission.DisposeCompleted();
                disposed.Add(submission);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        lock (_gate)
        {
            foreach (var submission in disposed)
                _pending.Remove(submission);

            if (errors.Count == 0)
            {
                _state = PendingTrackerState.Disposed;
                _pending.Clear();
            }
        }

        if (errors.Count > 0)
            throw new AggregateException("Failed to dispose one or more render submissions.", errors);
    }

    private static TimeSpan GetRemainingTime(long deadline)
    {
        var remainingTicks = deadline - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private sealed class SubmissionReferenceEqualityComparer : IEqualityComparer<IRenderFrameSubmission>
    {
        public static SubmissionReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(IRenderFrameSubmission? x, IRenderFrameSubmission? y) => ReferenceEquals(x, y);

        public int GetHashCode(IRenderFrameSubmission obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
