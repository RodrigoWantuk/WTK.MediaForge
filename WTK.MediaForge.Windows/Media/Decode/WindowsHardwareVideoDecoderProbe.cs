using WTK.MediaForge.Core.Media.Decode;

namespace WTK.MediaForge.Windows.Media.Decode;

public sealed class WindowsHardwareVideoDecoderProbe
{
    public IReadOnlyList<HardwareDecoderInfo> Probe()
    {
        // This intentionally returns no product decoder until real MF/D3D11VA
        // decode into a validated GPU surface is implemented.
        return Array.Empty<HardwareDecoderInfo>();
    }
}
