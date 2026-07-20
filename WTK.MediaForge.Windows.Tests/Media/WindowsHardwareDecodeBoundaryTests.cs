using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Windows.Media.Decode;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[Trait("Category", "GPU")]
public sealed class WindowsHardwareDecodeBoundaryTests
{
    [Fact]
    public async Task Public_decoder_rejects_placeholder_decode_path()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempVideo = Path.Combine(Path.GetTempPath(), $"mf-decode-blocked-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(tempVideo, MinimalMp4TestAsset.CreateAnnexBBytes());

        try
        {
            await using var decoder = new MediaFoundationHardwareVideoDecoder();
            var audit = new CollectingMediaTransportAuditSink();
            var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await decoder.OpenAsync(
                    new HardwareDecodeOpenContext
                    {
                        SourcePath = tempVideo,
                        Session = new HardwareDecodeSession
                        {
                            Codec = EncodedVideoCodec.H264,
                            Width = 640,
                            Height = 360
                        }
                    },
                    audit));

            Assert.Contains("IMFDXGIBuffer-backed GPU sample", ex.Message, StringComparison.Ordinal);
            Assert.False(audit.Contains(MediaTransportAuditEventKind.HardwareDecodeSucceeded));
        }
        finally
        {
            if (File.Exists(tempVideo))
                File.Delete(tempVideo);
        }
    }

}

internal static class MinimalMp4TestAsset
{
    public static byte[] CreateAnnexBBytes() =>
    [
        0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E, 0xAB, 0x40, 0xF0, 0x28, 0xD3, 0x70
    ];
}
