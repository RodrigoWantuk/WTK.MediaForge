using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media;

internal sealed class WindowsMediaCapabilitySnapshotProvider
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>> _reportFactory;
    private Task<MediaForgeCapabilitySnapshot>? _snapshotTask;
    private long _generation;

    public WindowsMediaCapabilitySnapshotProvider(
        Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>> reportFactory)
    {
        _reportFactory = reportFactory ?? throw new ArgumentNullException(nameof(reportFactory));
    }

    public async ValueTask<MediaForgeCapabilitySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            Task<MediaForgeCapabilitySnapshot> task;
            lock (_gate)
            {
                _snapshotTask ??= CaptureAsync(_generation);
                task = _snapshotTask;
            }

            try
            {
                var snapshot = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (snapshot.Generation == Volatile.Read(ref _generation))
                    return snapshot;
            }
            catch
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_snapshotTask, task))
                            _snapshotTask = null;
                    }
                }

                throw;
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            checked { _generation++; }
            _snapshotTask = null;
        }
    }

    private async Task<MediaForgeCapabilitySnapshot> CaptureAsync(long generation)
    {
        var report = await _reportFactory(CancellationToken.None).ConfigureAwait(false);
        var hardware = report.Hardware;
        return new MediaForgeCapabilitySnapshot
        {
            Generation = generation,
            CapturedAt = DateTimeOffset.UtcNow,
            Adapter = new MediaForgeHardwareAdapterInfo
            {
                Platform = hardware.Platform,
                AdapterId = hardware.AdapterId ?? "unavailable",
                DeviceName = hardware.DeviceName ?? "Unknown",
                Vendor = hardware.GpuVendor,
                DriverVersion = hardware.DriverVersion,
                DeviceGeneration = generation
            },
            Report = report
        };
    }
}
