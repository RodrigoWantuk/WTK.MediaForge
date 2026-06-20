using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Capture;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

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
}
