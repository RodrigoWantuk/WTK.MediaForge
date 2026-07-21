using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanIntermediateTargetPool : IDisposable
{
    private readonly VulkanGpuResourcePool _gpuResourcePool;
    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, List<Entry>> _entries = [];
    private int _disposed;

    internal int LiveEntryCountForTests
    {
        get
        {
            lock (_gate)
                return _entries.Values.Sum(static entries => entries.Count);
        }
    }

    public VulkanIntermediateTargetPool(VulkanGpuResourcePool gpuResourcePool) =>
        _gpuResourcePool = gpuResourcePool ?? throw new ArgumentNullException(nameof(gpuResourcePool));

    public VulkanOffscreenTargetHandle Rent(CanvasId canvasId, FrameSize size) =>
        Rent(ResolvedCanvasKey.Unversioned(canvasId), size);

    public VulkanOffscreenTargetHandle Rent(ResolvedCanvasKey canvasKey, FrameSize size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (canvasKey.IsEmpty)
            throw new ArgumentException("Resolved canvas key cannot be empty.", nameof(canvasKey));
        if (size.Width == 0 || size.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Intermediate target size must be non-zero.");

        Entry entry;
        List<Entry>? entriesToDispose;
        var key = new PoolKey(canvasKey, size);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            entriesToDispose = RetireEntriesForOtherSizes(canvasKey, size);

            if (!_entries.TryGetValue(key, out var entries))
            {
                entries = [];
                _entries.Add(key, entries);
            }

            entry = entries.FirstOrDefault(static candidate => candidate.ActiveBorrows == 0)
                ?? CreateEntry(entries, size);
            entry.ActiveBorrows++;
        }

        DisposeEntries(entriesToDispose);
        entry.Target.CurrentLayout = ImageLayout.Undefined;
        return new VulkanOffscreenTargetHandle(
            entry.Target,
            _ => ReleaseBorrow(key, entry));
    }

    public void InvalidateAll()
    {
        List<Entry>? disposeNow = null;
        lock (_gate)
        {
            foreach (var entry in _entries.Values.SelectMany(static entries => entries))
            {
                entry.Retired = true;
                if (entry.ActiveBorrows == 0)
                    (disposeNow ??= []).Add(entry);
            }

            _entries.Clear();
        }

        DisposeEntries(disposeNow);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        InvalidateAll();
    }

    private Entry CreateEntry(ICollection<Entry> entries, FrameSize size)
    {
        var acquired = _gpuResourcePool.AcquireOffscreenTarget(size, GpuTextureUsage.Intermediate);
        var entry = new Entry(acquired.Lease, acquired.Target);
        entries.Add(entry);
        return entry;
    }

    private void ReleaseBorrow(PoolKey key, Entry entry)
    {
        GpuTextureLease? leaseToDispose = null;
        lock (_gate)
        {
            if (entry.ActiveBorrows <= 0)
                throw new InvalidOperationException("Intermediate target borrow was released more times than retained.");

            entry.ActiveBorrows--;
            if (entry.ActiveBorrows == 0 && entry.Retired)
            {
                RemoveEntry(key, entry);
                leaseToDispose = entry.Lease;
            }
        }

        leaseToDispose?.Dispose();
    }

    private List<Entry>? RetireEntriesForOtherSizes(ResolvedCanvasKey canvasKey, FrameSize size)
    {
        List<Entry>? disposeNow = null;
        foreach (var pair in _entries
                     .Where(pair => pair.Key.CanvasKey == canvasKey && pair.Key.Size != size)
                     .ToArray())
        {
            foreach (var entry in pair.Value)
            {
                entry.Retired = true;
                if (entry.ActiveBorrows == 0)
                    (disposeNow ??= []).Add(entry);
            }

            _entries.Remove(pair.Key);
        }

        return disposeNow;
    }

    private void RemoveEntry(PoolKey key, Entry entry)
    {
        if (!_entries.TryGetValue(key, out var entries))
            return;

        entries.Remove(entry);
        if (entries.Count == 0)
            _entries.Remove(key);
    }

    private static void DisposeEntries(IEnumerable<Entry>? entries)
    {
        if (entries is null)
            return;

        foreach (var entry in entries)
            entry.Lease.Dispose();
    }

    private readonly record struct PoolKey(ResolvedCanvasKey CanvasKey, FrameSize Size);

    private sealed class Entry(GpuTextureLease lease, VulkanOffscreenRenderTarget target)
    {
        public GpuTextureLease Lease { get; } = lease;

        public VulkanOffscreenRenderTarget Target { get; } = target;

        public int ActiveBorrows { get; set; }

        public bool Retired { get; set; }
    }
}
