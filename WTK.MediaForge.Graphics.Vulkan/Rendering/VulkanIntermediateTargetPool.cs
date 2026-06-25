using Silk.NET.Vulkan;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanIntermediateTargetPool : IDisposable
{
    private readonly VulkanHeadlessDevice _device;
    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, VulkanOffscreenRenderTarget> _entries = [];
    private int _disposed;

    internal int LiveEntryCountForTests
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public VulkanIntermediateTargetPool(VulkanHeadlessDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public VulkanOffscreenTargetHandle Rent(CanvasId canvasId, FrameSize size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (canvasId.IsEmpty)
            throw new ArgumentException("Canvas id cannot be empty.", nameof(canvasId));

        if (size.Width == 0 || size.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Intermediate target size must be non-zero.");

        VulkanOffscreenRenderTarget target;
        lock (_gate)
        {
            RemoveStaleEntriesForCanvas(canvasId, size);

            var key = new PoolKey(canvasId, size);
            if (_entries.TryGetValue(key, out var existing))
            {
                target = existing;
            }
            else
            {
                target = new VulkanOffscreenRenderTarget(_device, size);
                _entries[key] = target;
            }
        }

        target.CurrentLayout = ImageLayout.Undefined;
        return new VulkanOffscreenTargetHandle(target, ReleaseRentedTarget);
    }

    public void InvalidateAll()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        lock (_gate)
        {
            foreach (var target in _entries.Values)
                target.Dispose();

            _entries.Clear();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        InvalidateAll();
    }

    private void ReleaseRentedTarget(IVulkanOffscreenRenderTarget target)
    {
        _ = target;
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
            if (_entries.Remove(key, out var staleTarget))
                staleTarget.Dispose();
        }
    }

    private readonly record struct PoolKey(CanvasId CanvasId, FrameSize Size);
}
