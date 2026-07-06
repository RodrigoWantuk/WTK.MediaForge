namespace WTK.MediaForge.Core.Gpu.Resources;

/// <summary>
/// Public lease for GPU texture lifetime. Does not expose backend-native handles.
/// </summary>
public sealed class GpuTextureLease : IDisposable
{
    private GpuTexture? _texture;
    private readonly GpuResourcePool _pool;
    private int _disposed;

    internal GpuTextureLease(GpuTexture texture, GpuResourcePool pool)
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        texture.AddLeaseRef();
    }

    public GpuTextureId TextureId => new(_texture?.Id.Value ?? Guid.Empty);

    public int Width => _texture?.Descriptor.Width ?? 0;

    public int Height => _texture?.Descriptor.Height ?? 0;

    public string Format => _texture?.Descriptor.Format ?? string.Empty;

    internal GpuTexture Texture =>
        _texture ?? throw new ObjectDisposedException(nameof(GpuTextureLease));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var texture = Interlocked.Exchange(ref _texture, null);
        if (texture is null)
            return;

        _pool.Release(texture);
    }
}
