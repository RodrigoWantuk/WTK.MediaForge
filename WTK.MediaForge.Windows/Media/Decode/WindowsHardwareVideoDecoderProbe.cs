using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Decode;

namespace WTK.MediaForge.Windows.Media.Decode;

public sealed class WindowsHardwareVideoDecoderProbe
{
    public IReadOnlyList<HardwareDecoderInfo> Probe()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<HardwareDecoderInfo>();

        return
        [
            new HardwareDecoderInfo
            {
                Name = "Media Foundation H.264 Hardware MFT",
                Codec = EncodedVideoCodec.H264,
                Backend = "MediaFoundation-D3D11VA",
                ProducesGpuSurface = true
            }
        ];
    }
}
