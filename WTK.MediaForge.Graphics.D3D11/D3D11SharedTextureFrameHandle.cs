using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Graphics.D3D11;

public sealed class D3D11SharedTextureFrameHandle : IGpuFrameHandle, IDisposable
{
    private int _disposed;
    private int _isRetired;
    private ulong _producerAcquireKey = D3D11SharedTextureSyncKeys.Producer;

    internal D3D11SharedTextureFrameHandle(
        ID3D11Texture2D texture,
        IDXGIKeyedMutex keyedMutex,
        SharedWin32Handle sharedHandle,
        FrameSize textureSize,
        Format format,
        GpuTextureId textureId)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        KeyedMutex = keyedMutex ?? throw new ArgumentNullException(nameof(keyedMutex));
        SharedHandle = sharedHandle ?? throw new ArgumentNullException(nameof(sharedHandle));
        TextureSize = textureSize;
        Format = format;
        TextureId = textureId;
    }

    public GpuFrameBackend Backend => GpuFrameBackend.D3D11SharedTexture;

    public ID3D11Texture2D Texture { get; private set; }

    public IDXGIKeyedMutex KeyedMutex { get; private set; }

    public SharedWin32Handle SharedHandle { get; }

    public FrameSize TextureSize { get; }

    public Format Format { get; }

    public GpuTextureId TextureId { get; }

    public bool HasSharedHandle => !SharedHandle.IsInvalid;

    public ulong ProducerAcquireKey => Volatile.Read(ref _producerAcquireKey);

    public bool IsRetired => Volatile.Read(ref _isRetired) != 0;

    public void MarkRetired() => Volatile.Write(ref _isRetired, 1);

    public void NotifyCaptureReleasedToConsumer() =>
        Volatile.Write(ref _producerAcquireKey, D3D11SharedTextureSyncKeys.Consumer);

    public void NotifyVulkanReleasedToProducer() =>
        Volatile.Write(ref _producerAcquireKey, D3D11SharedTextureSyncKeys.Producer);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        KeyedMutex.Dispose();
        KeyedMutex = null!;

        Texture.Dispose();
        Texture = null!;

        SharedHandle.Dispose();
    }
}
