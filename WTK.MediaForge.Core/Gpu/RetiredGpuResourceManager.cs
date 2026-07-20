using System.Diagnostics;

namespace WTK.MediaForge.Core.Gpu;

public sealed record RetiredGpuResourceFailure(
    IRetiredGpuResource Resource,
    Exception Exception,
    DateTimeOffset Timestamp);

public sealed record RetiredGpuResourcePendingState(
    IRetiredGpuResource Resource,
    string DiagnosticName,
    string State,
    DateTimeOffset RetiredAt,
    int FinalizationAttempts);

public sealed class RetiredGpuResourceManager
{
    private readonly List<IRetiredGpuResource> _pending = [];
    private readonly List<RetiredGpuResourceFailure> _failed = [];
    private readonly Dictionary<IRetiredGpuResource, PendingMetadata> _metadata =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    public int FailedCount
    {
        get
        {
            lock (_gate)
                return _failed.Count;
        }
    }

    public IReadOnlyList<RetiredGpuResourceFailure> Failures
    {
        get
        {
            lock (_gate)
                return _failed.ToArray();
        }
    }

    public IReadOnlyList<RetiredGpuResourcePendingState> PendingStates
    {
        get
        {
            lock (_gate)
            {
                return _pending
                    .Select(CreatePendingStateLocked)
                    .ToArray();
            }
        }
    }

    internal IReadOnlyList<IRetiredGpuResource> PendingResources
    {
        get
        {
            lock (_gate)
                return _pending.ToArray();
        }
    }

    internal void RequeueFailedResourcesForRetry()
    {
        lock (_gate)
        {
            foreach (var failure in _failed)
            {
                if (!_pending.Any(r => ReferenceEquals(r, failure.Resource)))
                {
                    _pending.Add(failure.Resource);
                    _metadata[failure.Resource] = new PendingMetadata(DateTimeOffset.UtcNow);
                }
            }

            _failed.Clear();
        }
    }

    public void Add(IRetiredGpuResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        lock (_gate)
        {
            if (!_pending.Any(r => ReferenceEquals(r, resource)))
            {
                _pending.Add(resource);
                _metadata[resource] = new PendingMetadata(DateTimeOffset.UtcNow);
            }
        }

        TryFinalizeAll();
    }

    public void TryFinalizeAll()
    {
        IRetiredGpuResource[] snapshot;

        lock (_gate)
            snapshot = _pending.ToArray();

        foreach (var resource in snapshot)
        {
            try
            {
                lock (_gate)
                {
                    if (_metadata.TryGetValue(resource, out var metadata))
                        metadata.FinalizationAttempts++;
                }

                resource.TryFinalizePhysicalResources();
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (_metadata.TryGetValue(resource, out var metadata))
                        metadata.LastException = ex;
                }
            }
        }

        var completed = new List<IRetiredGpuResource>();
        var failed = new List<RetiredGpuResourceFailure>();

        lock (_gate)
        {
            foreach (var resource in _pending)
            {
                if (resource.FullyDisposed.IsCompletedSuccessfully)
                {
                    completed.Add(resource);
                    continue;
                }

                if (resource.FullyDisposed.IsFaulted &&
                    !_failed.Any(f => ReferenceEquals(f.Resource, resource)))
                {
                    failed.Add(new RetiredGpuResourceFailure(
                        resource,
                        FlattenException(resource.FullyDisposed.Exception),
                        DateTimeOffset.UtcNow));
                    continue;
                }

                if (_metadata.TryGetValue(resource, out var metadata) &&
                    metadata.LastException is { } finalizationException &&
                    !_failed.Any(f => ReferenceEquals(f.Resource, resource)))
                {
                    failed.Add(new RetiredGpuResourceFailure(
                        resource,
                        finalizationException,
                        DateTimeOffset.UtcNow));
                }
            }

            foreach (var resource in completed)
            {
                _pending.Remove(resource);
                _metadata.Remove(resource);
            }

            foreach (var failure in failed)
            {
                _pending.Remove(failure.Resource);
                _metadata.Remove(failure.Resource);
                _failed.Add(failure);
            }
        }
    }

    public async ValueTask WaitForAllFinalizedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var timeoutTicks = (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        var deadline = start + timeoutTicks;

        while (GetRemainingTime(deadline) > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryFinalizeAll();

            List<Exception> faultedExceptions;
            lock (_gate)
            {
                if (_failed.Count > 0)
                {
                    faultedExceptions = _failed.Select(f => f.Exception).ToList();
                }
                else if (_pending.Count == 0)
                {
                    return;
                }
                else
                {
                    faultedExceptions = [];
                }
            }

            if (faultedExceptions.Count > 0)
                throw new AggregateException("One or more retired GPU resources failed to finalize.", faultedExceptions);

            IRetiredGpuResource[] pendingSnapshot;
            lock (_gate)
                pendingSnapshot = _pending.ToArray();

            foreach (var resource in pendingSnapshot)
            {
                if (resource.FullyDisposed.IsFaulted)
                {
                    TryFinalizeAll();

                    lock (_gate)
                    {
                        if (_failed.Count > 0)
                        {
                            throw new AggregateException(
                                "One or more retired GPU resources failed to finalize.",
                                _failed.Select(f => f.Exception));
                        }
                    }
                }
            }

            var waitTasks = pendingSnapshot
                .Select(r => r.FullyDisposed)
                .ToArray();

            var remaining = GetRemainingTime(deadline);
            if (remaining <= TimeSpan.Zero)
                break;

            var delayMs = (int)Math.Min(remaining.TotalMilliseconds, 50);
            var waitTask = Task.WhenAll(waitTasks);
            var completed = await Task.WhenAny(waitTask, Task.Delay(delayMs, cancellationToken))
                .ConfigureAwait(false);

            if (completed == waitTask)
            {
                TryFinalizeAll();

                lock (_gate)
                {
                    if (_failed.Count > 0)
                    {
                        throw new AggregateException(
                            "One or more retired GPU resources failed to finalize.",
                            _failed.Select(f => f.Exception));
                    }

                    if (_pending.Count == 0)
                        return;
                }
            }
        }

        TryFinalizeAll();

        lock (_gate)
        {
            if (_failed.Count > 0)
            {
                throw new AggregateException(
                    "One or more retired GPU resources failed to finalize.",
                    _failed.Select(f => f.Exception));
            }

            if (_pending.Count > 0)
            {
                var pendingDetails = string.Join(
                    Environment.NewLine,
                    _pending.Select(resource => FormatPendingState(CreatePendingStateLocked(resource))));
                throw new TimeoutException(
                    $"Retired GPU resources were not finalized before timeout.{Environment.NewLine}{pendingDetails}");
            }
        }
    }

    private RetiredGpuResourcePendingState CreatePendingStateLocked(IRetiredGpuResource resource)
    {
        var metadata = _metadata.TryGetValue(resource, out var value)
            ? value
            : new PendingMetadata(DateTimeOffset.UtcNow);
        var diagnostics = resource as IRetiredGpuResourceDiagnostics;
        var diagnosticName = diagnostics?.DiagnosticName ?? resource.GetType().Name;
        string state;

        try
        {
            state = diagnostics?.DescribeState() ??
                    $"FullyDisposed={resource.FullyDisposed.Status}";
        }
        catch (Exception ex)
        {
            state = $"State inspection failed: {ex.GetType().Name}: {ex.Message}";
        }

        return new RetiredGpuResourcePendingState(
            resource,
            diagnosticName,
            state,
            metadata.RetiredAt,
            metadata.FinalizationAttempts);
    }

    private static string FormatPendingState(RetiredGpuResourcePendingState state) =>
        $"- {state.DiagnosticName}: {state.State}; retiredAt={state.RetiredAt:O}; attempts={state.FinalizationAttempts}.";

    private static Exception FlattenException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            aggregate = aggregate.Flatten();
            if (aggregate.InnerExceptions.Count == 1)
                return aggregate.InnerExceptions[0];
        }

        return exception;
    }

    private static TimeSpan GetRemainingTime(long deadline)
    {
        var remainingTicks = deadline - Stopwatch.GetTimestamp();

        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private sealed class PendingMetadata(DateTimeOffset retiredAt)
    {
        public DateTimeOffset RetiredAt { get; } = retiredAt;

        public int FinalizationAttempts { get; set; }

        public Exception? LastException { get; set; }
    }
}
