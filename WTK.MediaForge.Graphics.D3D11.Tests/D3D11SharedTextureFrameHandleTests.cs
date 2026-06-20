using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Graphics.D3D11.Tests;

public class D3D11SharedTextureFrameHandleTests
{
    [Fact]
    public void CreateSharedTexture_produces_importable_handle_with_keyed_mutex()
    {
        if (!TryCreateDefaultDevice(out var device))
        {
            return;
        }

        using (device)
        {
            using var handle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 64,
                height: 64,
                Format.B8G8R8A8_UNorm);

            Assert.Equal(GpuFrameBackend.D3D11SharedTexture, handle.Backend);
            Assert.True(handle.HasSharedHandle);
            Assert.False(handle.SharedHandle.IsInvalid);
            Assert.Equal(64u, handle.TextureSize.Width);
            Assert.Equal(64u, handle.TextureSize.Height);
            Assert.Equal(Format.B8G8R8A8_UNorm, handle.Format);
            Assert.NotNull(handle.Texture);
            Assert.NotNull(handle.KeyedMutex);
        }
    }

    [Fact]
    public void Dispose_releases_texture_and_mutex()
    {
        if (!TryCreateDefaultDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var handle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 32,
                height: 32);

            handle.Dispose();
            handle.Dispose();
        }
    }

    [Fact]
    public void CreateSharedTexture_rejects_zero_dimensions()
    {
        if (!TryCreateDefaultDevice(out var device))
        {
            return;
        }

        using (device)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 0, height: 64));
        }
    }

    private static bool TryCreateDefaultDevice(out D3D11GpuDevice device)
    {
        device = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
