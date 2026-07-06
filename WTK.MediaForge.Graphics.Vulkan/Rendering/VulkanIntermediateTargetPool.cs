using Silk.NET.Vulkan;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanIntermediateTargetPool : IDisposable
{
    private readonly VulkanGpuResourcePool _gpuResourcePool;
    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, Entry> _entries = [];
    private int _disposed;

    internal int LiveEntryCountForTests
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public VulkanIntermediateTargetPool(VulkanGpuResourcePool gpuResourcePool) =>
        _gpuResourcePool = gpuResourcePool ?? throw new ArgumentNullException(nameof(gpuResourcePool));

    public VulkanOffscreenTargetHandle Rent(CanvasId canvasId, FrameSize size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (canvasId.IsEmpty)
            throw new ArgumentException("Canvas id cannot be empty.", nameof(canvasId));

        if (size.Width == 0 || size.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Intermediate target size must be non-zero.");

        Entry entry;
        lock (_gate)
        {
            RemoveStaleEntriesForCanvas(canvasId, size);

            var key = new PoolKey(canvasId, size);
            if (!_entries.TryGetValue(key, out var existingEntry))
            {
                var acquired = _gpuResourcePool.AcquireOffscreenTarget(size, GpuTextureUsage.Intermediate);
                existingEntry = new Entry(acquired.Lease, acquired.Target);
                _entries[key] = existingEntry;
            }

            entry = existingEntry;
        }

        entry.Target.CurrentLayout = ImageLayout.Undefined;
        return new VulkanOffscreenTargetHandle(entry.Target, static _ => { });
    }

    public void InvalidateAll()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        List<Entry> entries;
        lock (_gate)
        {
            entries = _entries.Values.ToList();
            _entries.Clear();
        }

        foreach (var entry in entries)
            entry.Lease.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        InvalidateAll();
    }

    private void RemoveStaleEntriesForCanvas(CanvasId canvasId, FrameSize size)
    {
        List<PoolKey>? staleKeys = null;

        foreach (var entry in _entries)
        {
            if (entry.Key.CanvasId != canvasId || entry.Key.Size == size)
                continue;

            (staleKeys ??= []).Add(entry.Key);
        }

        if (staleKeys is null)
            return;

        foreach (var key in staleKeys)
        {
            if (_entries.Remove(key, out var staleEntry))
                staleEntry.Lease.Dispose();
        }
    }

    private readonly record struct PoolKey(CanvasId CanvasId, FrameSize Size);

    private sealed record Entry(GpuTextureLease Lease, VulkanOffscreenRenderTarget Target);
}
