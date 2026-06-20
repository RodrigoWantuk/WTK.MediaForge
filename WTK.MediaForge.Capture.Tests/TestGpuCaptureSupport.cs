using Vortice.DXGI;
using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.Tests;

internal static class TestGpuCaptureSupport
{
    public static bool TryGetPrimaryCaptureSource(out CaptureSourceInfo source)
    {
        source = null!;

        try
        {
            var monitors = DesktopMonitorEnumerator.Enumerate();
            if (monitors.Count == 0)
                return false;

            source = monitors[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryCreateDefaultDevice(out D3D11GpuDevice device)
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
