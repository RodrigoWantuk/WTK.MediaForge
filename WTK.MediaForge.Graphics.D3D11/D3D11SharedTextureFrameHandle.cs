using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Graphics.D3D11;

public sealed class D3D11SharedTextureFrameHandle : IGpuFrameHandle, IDisposable
{
    private int _disposed;

    internal D3D11SharedTextureFrameHandle(
        ID3D11Texture2D texture,
        IDXGIKeyedMutex keyedMutex,
        nint sharedHandle,
        FrameSize textureSize,
        Format format)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        KeyedMutex = keyedMutex ?? throw new ArgumentNullException(nameof(keyedMutex));
        SharedHandle = sharedHandle;
        TextureSize = textureSize;
        Format = format;
    }

    public GpuFrameBackend Backend => GpuFrameBackend.D3D11SharedTexture;

    public ID3D11Texture2D Texture { get; private set; }

    public IDXGIKeyedMutex KeyedMutex { get; private set; }

    public nint SharedHandle { get; }

    public FrameSize TextureSize { get; }

    public Format Format { get; }

    public bool HasSharedHandle => SharedHandle != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        KeyedMutex.Dispose();
        KeyedMutex = null!;

        Texture.Dispose();
        Texture = null!;
    }
}
