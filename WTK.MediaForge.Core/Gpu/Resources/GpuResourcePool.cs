namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuResourcePool : IDisposable
{
    private readonly IGpuTextureFactory _textureFactory;
    private readonly RetiredGpuResourceManager _retiredResources = new();
    private readonly object _gate = new();
    private readonly Dictionary<GpuResourcePoolKey, Stack<GpuTexture>> _availableTextures = new();
    private readonly Dictionary<GpuResourceId, GpuTexture> _textures = new();
    private readonly List<GpuTexture> _pendingFenceTextures = [];
    private int _physicalHighWaterMark;
    private int _disposed;

    public GpuResourcePool(IGpuTextureFactory textureFactory) =>
        _textureFactory = textureFactory ?? throw new ArgumentNullException(nameof(textureFactory));

    public RetiredGpuResourceManager RetiredResources => _retiredResources;

    internal int ActiveTextureCount
    {
        get
        {
            lock (_gate)
                return _textures.Count;
        }
    }

    internal int AvailableTextureCount
    {
        get
        {
            lock (_gate)
                return _availableTextures.Values.Sum(stack => stack.Count);
        }
    }

    internal int PendingFenceTextureCount
    {
        get
        {
            lock (_gate)
                return _pendingFenceTextures.Count;
        }
    }

    internal int PhysicalHighWaterMark
    {
        get
        {
            lock (_gate)
                return _physicalHighWaterMark;
        }
    }

    public GpuTextureLease AcquireTexture(GpuTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Width <= 0 || descriptor.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Texture dimensions must be greater than zero.");
        }

        lock (_gate)
        {
            PromoteSignaledPendingTextures();

            var key = GpuResourcePoolKey.From(descriptor);
            if (descriptor.Recyclable &&
                _availableTextures.TryGetValue(key, out var stack))
            {
                while (stack.Count > 0)
                {
                    var recycled = stack.Pop();
                    if (!IsRecyclable(recycled))
                    {
                        RetireTexture(recycled);
                        continue;
                    }

                    recycled.MarkActive();
                    _textures[recycled.Id] = recycled;
                    return new GpuTextureLease(recycled, this);
                }
            }
        }

        var physical = _textureFactory.CreateTexture(descriptor);
        var texture = new GpuTexture(descriptor, physical);

        lock (_gate)
        {
            _textures[texture.Id] = texture;
            _physicalHighWaterMark = Math.Max(
                _physicalHighWaterMark,
                _textures.Count +
                _availableTextures.Values.Sum(static stack => stack.Count) +
                _pendingFenceTextures.Count);
        }

        return new GpuTextureLease(texture, this);
    }

    internal void Release(GpuTexture texture)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(texture);

        if (!texture.ReleaseLeaseRef())
            return;

        lock (_gate)
            _textures.Remove(texture.Id);

        if (texture.Descriptor.Recyclable && IsRecyclable(texture))
        {
            ReturnToAvailable(texture);
            return;
        }

        if (texture.Descriptor.Recyclable &&
            texture.RetirementFence is not null &&
            !texture.RetirementFence.IsSignaled)
        {
            lock (_gate)
                _pendingFenceTextures.Add(texture);

            return;
        }

        RetireTexture(texture);
    }

    public void CollectRetired()
    {
        lock (_gate)
            PromoteSignaledPendingTextures();

        _retiredResources.TryFinalizeAll();
    }

    private void ReturnToAvailable(GpuTexture texture)
    {
        texture.MarkActive();
        var key = GpuResourcePoolKey.From(texture.Descriptor);

        lock (_gate)
        {
            if (!_availableTextures.TryGetValue(key, out var stack))
            {
                stack = new Stack<GpuTexture>();
                _availableTextures[key] = stack;
            }

            stack.Push(texture);
        }
    }

    private void PromoteSignaledPendingTextures()
    {
        for (var index = _pendingFenceTextures.Count - 1; index >= 0; index--)
        {
            var pending = _pendingFenceTextures[index];
            if (!IsRecyclable(pending))
                continue;

            _pendingFenceTextures.RemoveAt(index);
            ReturnToAvailable(pending);
        }
    }

    public ValueTask WaitForRetiredAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _retiredResources.WaitForAllFinalizedAsync(timeout, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<GpuTexture> toRetire;

        lock (_gate)
        {
            toRetire =
            [
                .._textures.Values,
                .._availableTextures.Values.SelectMany(stack => stack),
                .._pendingFenceTextures
            ];

            _textures.Clear();
            _availableTextures.Clear();
            _pendingFenceTextures.Clear();
        }

        foreach (var texture in toRetire)
            RetireTexture(texture);

        _retiredResources.TryFinalizeAll();
    }

    private static bool IsRecyclable(GpuTexture texture)
    {
        if (texture.RetirementFence is null)
            return true;

        return texture.RetirementFence.IsSignaled;
    }

    private void RetireTexture(GpuTexture texture)
    {
        texture.MarkRetired();

        if (texture.Physical is not null)
            _retiredResources.Add(texture.Physical);
    }
}
