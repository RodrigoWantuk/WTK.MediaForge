using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using SharedResourceFlags = Vortice.DXGI.SharedResourceFlags;

namespace WTK.MediaForge.Graphics.D3D11;

public static class D3D11SharedTextureFactory
{
    public static D3D11SharedTextureFrameHandle CreateSharedTexture(
        ID3D11Device device,
        uint width,
        uint height,
        Format format = Format.B8G8R8A8_UNorm)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be greater than zero.");

        var description = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags =
                ResourceOptionFlags.SharedNTHandle |
                ResourceOptionFlags.SharedKeyedMutex
        };

        var texture = device.CreateTexture2D(description);
        var rawSharedHandle = CreateSharedHandle(texture);

        if (rawSharedHandle == 0)
        {
            texture.Dispose();
            throw new InvalidOperationException("Failed to create D3D11 shared NT handle.");
        }

        var sharedHandle = SharedWin32Handle.FromOwnedRaw(rawSharedHandle);

        var keyedMutex = texture.QueryInterface<IDXGIKeyedMutex>();

        return new D3D11SharedTextureFrameHandle(
            texture,
            keyedMutex,
            sharedHandle,
            new FrameSize(width, height),
            format,
            GpuTextureId.New());
    }

    private static nint CreateSharedHandle(ID3D11Texture2D texture)
    {
        using IDXGIResource1 resource = texture.QueryInterface<IDXGIResource1>();

        return resource.CreateSharedHandle(
            null,
            SharedResourceFlags.Read | SharedResourceFlags.Write,
            null);
    }
}
