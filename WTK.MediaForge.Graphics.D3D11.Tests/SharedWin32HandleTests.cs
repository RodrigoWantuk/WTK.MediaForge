using Vortice.DXGI;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Graphics.D3D11.Tests;

public class SharedWin32HandleTests
{
    [Fact]
    public void DuplicateFrom_creates_independent_owned_handle()
    {
        if (!TryCreateDefaultDevice(out var device))
            return;

        using (device)
        {
            using var handle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 16,
                height: 16);

            using var duplicate = SharedWin32Handle.DuplicateFrom(handle.SharedHandle);

            Assert.False(handle.SharedHandle.IsInvalid);
            Assert.False(duplicate.IsInvalid);
            Assert.NotSame(handle.SharedHandle, duplicate);
            Assert.NotEqual(
                handle.SharedHandle.DangerousGetHandleForInterop(),
                duplicate.DangerousGetHandleForInterop());
        }
    }

    [Fact]
    public void Dispose_closes_duplicated_handle()
    {
        if (!TryCreateDefaultDevice(out var device))
            return;

        using (device)
        {
            using var handle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 16,
                height: 16);

            var duplicate = SharedWin32Handle.DuplicateFrom(handle.SharedHandle);
            duplicate.Dispose();

            Assert.True(duplicate.IsClosed);
        }
    }

    [Fact]
    public void DuplicateFrom_throws_for_invalid_handle()
    {
        if (!TryCreateDefaultDevice(out var device))
            return;

        using (device)
        {
            using var handle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 16,
                height: 16);

            var duplicate = SharedWin32Handle.DuplicateFrom(handle.SharedHandle);
            duplicate.Dispose();

            Assert.Throws<InvalidOperationException>(() => SharedWin32Handle.DuplicateFrom(duplicate));
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
