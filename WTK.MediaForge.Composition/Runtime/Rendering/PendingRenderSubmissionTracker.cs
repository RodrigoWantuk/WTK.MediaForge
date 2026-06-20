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
        {
            try
            {
                submission.Dispose();
            }
            catch (Exception)
            {
                // TODO: Diagnostics.Record submission dispose failure.
            }
        }
    }

    public void Dispose()
    {
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
            try
            {
                submission.Dispose();
            }
            catch (Exception)
            {
                // TODO: Diagnostics.Record submission dispose failure.
            }
        }
    }
}
