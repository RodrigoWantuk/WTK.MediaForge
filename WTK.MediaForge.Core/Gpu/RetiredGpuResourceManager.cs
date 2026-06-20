using System.Diagnostics;

namespace WTK.MediaForge.Core.Gpu;

public sealed class RetiredGpuResourceManager
{
    private readonly List<IRetiredGpuResource> _resources = [];
    private readonly object _gate = new();

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _resources.Count;
        }
    }

    internal IReadOnlyList<IRetiredGpuResource> PendingResources
    {
        get
        {
            lock (_gate)
                return _resources.ToArray();
        }
    }

    public void Add(IRetiredGpuResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        lock (_gate)
        {
            if (!_resources.Any(r => ReferenceEquals(r, resource)))
                _resources.Add(resource);
        }

        TryFinalizeAll();
    }

    public void TryFinalizeAll()
    {
        IRetiredGpuResource[] snapshot;

        lock (_gate)
            snapshot = _resources.ToArray();

        foreach (var resource in snapshot)
        {
            try
            {
                resource.TryFinalizePhysicalResources();
            }
            catch (Exception)
            {
                // Fase 7: diagnostics. Resource stays until FullyDisposed completes or faults.
            }
        }

        lock (_gate)
            _resources.RemoveAll(r => r.FullyDisposed.IsCompletedSuccessfully);
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

            IRetiredGpuResource[] pending;
            lock (_gate)
            {
                if (_resources.Count == 0)
                    return;

                pending = _resources.ToArray();
            }

            foreach (var resource in pending)
            {
                if (resource.FullyDisposed.IsFaulted)
                    await resource.FullyDisposed.ConfigureAwait(false);
            }

            var waitTasks = pending
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
                foreach (var resource in pending)
                {
                    if (resource.FullyDisposed.IsFaulted)
                        await resource.FullyDisposed.ConfigureAwait(false);
                }

                TryFinalizeAll();

                lock (_gate)
                {
                    if (_resources.Count == 0)
                        return;
                }
            }
        }

        lock (_gate)
        {
            if (_resources.Count > 0)
            {
                throw new TimeoutException(
                    "Retired GPU resources were not finalized before timeout.");
            }
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
