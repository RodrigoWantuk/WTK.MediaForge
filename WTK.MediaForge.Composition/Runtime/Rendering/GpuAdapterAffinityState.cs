using WTK.MediaForge.Core.Capture;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal readonly record struct GpuAdapterAffinitySnapshot(
    GpuAdapterLuid AdapterLuid,
    string DeviceName,
    long DeviceGeneration)
{
    public bool IsAvailable => !AdapterLuid.IsEmpty;
}

internal sealed class GpuAdapterAffinityState
{
    private readonly object _gate = new();
    private GpuAdapterAffinitySnapshot _snapshot;

    public event Action<long>? GenerationChanged;

    public GpuAdapterAffinitySnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    public void Publish(GpuAdapterLuid adapterLuid, string deviceName)
    {
        if (adapterLuid.IsEmpty)
            throw new ArgumentException("A valid GPU adapter LUID is required.", nameof(adapterLuid));

        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        lock (_gate)
        {
            var nextGeneration = checked(_snapshot.DeviceGeneration + 1);
            _snapshot = new GpuAdapterAffinitySnapshot(adapterLuid, deviceName, nextGeneration);
        }

        GenerationChanged?.Invoke(Snapshot.DeviceGeneration);
    }

    public void Invalidate()
    {
        long generation;
        lock (_gate)
        {
            generation = checked(_snapshot.DeviceGeneration + 1);
            _snapshot = new GpuAdapterAffinitySnapshot(default, string.Empty, generation);
        }

        GenerationChanged?.Invoke(generation);
    }
}
