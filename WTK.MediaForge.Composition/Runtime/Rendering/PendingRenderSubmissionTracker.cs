using System.Diagnostics;
using System.Runtime.CompilerServices;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Tracks in-flight GPU render submissions without blocking on completion.
/// Shutdown uses <see cref="ShutdownAsync"/> to wait with timeouts before destroying resources.
/// </summary>
public class PendingRenderSubmissionTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IRenderFrameSubmission> _pending = [];
    private readonly HashSet<IRenderFrameSubmission> _cleanupFailureReported =
        new(SubmissionReferenceEqualityComparer.Instance);
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

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
            completed = _pending.Where(static submission => submission.IsCompleted).ToList();

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
            TryDisposeCompleted(submission, reportFailure: false, propagateFailure: true);
        }
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

            TryDisposeCompleted(submission, reportFailure: false, propagateFailure: true);
        }
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
