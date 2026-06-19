using Vortice.DXGI;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using ModeRotation = Vortice.DXGI.ModeRotation;

namespace WTK.MediaForge.Capture.DesktopDuplication;

public static class DesktopMonitorEnumerator
{
    public static IReadOnlyList<CaptureSourceInfo> Enumerate()
    {
        var result = new List<CaptureSourceInfo>();

        using IDXGIFactory1 factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            if (factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Failure)
                break;

            using (adapter)
            {
                var adapterDescription = adapter.Description1;

                for (uint outputIndex = 0; ; outputIndex++)
                {
                    if (adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).Failure)
                        break;

                    using (output)
                    {
                        var outputDescription = output.Description;
                        var adapterLuid = adapterDescription.Luid;

                        int width = outputDescription.DesktopCoordinates.Right - outputDescription.DesktopCoordinates.Left;
                        int height = outputDescription.DesktopCoordinates.Bottom - outputDescription.DesktopCoordinates.Top;

                        if (width < 0)
                            width = 0;

                        if (height < 0)
                            height = 0;

                        var desktopRect = new DesktopRect(
                            outputDescription.DesktopCoordinates.Left,
                            outputDescription.DesktopCoordinates.Top,
                            outputDescription.DesktopCoordinates.Right,
                            outputDescription.DesktopCoordinates.Bottom);

                        result.Add(new CaptureSourceInfo
                        {
                            AdapterIndex = adapterIndex,
                            OutputIndex = outputIndex,
                            AdapterName = adapterDescription.Description,
                            OutputName = outputDescription.DeviceName,
                            AdapterLuid = new GpuAdapterLuid
                            {
                                LowPart = adapterLuid.LowPart,
                                HighPart = adapterLuid.HighPart
                            },
                            DesktopRect = desktopRect,
                            LogicalSize = new FrameSize((uint)width, (uint)height),
                            Rotation = MapRotation(outputDescription.Rotation)
                        });
                    }
                }
            }
        }

        return result;
    }

    private static DisplayRotation MapRotation(ModeRotation rotation) =>
        rotation switch
        {
            ModeRotation.Rotate90 => DisplayRotation.Rotate90,
            ModeRotation.Rotate180 => DisplayRotation.Rotate180,
            ModeRotation.Rotate270 => DisplayRotation.Rotate270,
            _ => DisplayRotation.None
        };
}