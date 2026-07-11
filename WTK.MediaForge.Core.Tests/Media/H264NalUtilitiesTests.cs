using WTK.MediaForge.Core.Media.Encode;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class H264NalUtilitiesTests
{
    [Fact]
    public void ExtractAnnexBNalUnits_returns_nal_payloads_without_start_codes()
    {
        byte[] annexB =
        [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E,
            0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80,
            0x00, 0x00, 0x00, 0x01, 0x65, 0x88
        ];

        var units = H264NalUtilities.ExtractAnnexBNalUnits(annexB);

        Assert.Collection(
            units,
            sps => Assert.Equal<byte>([0x67, 0x42, 0x00, 0x1E], sps),
            pps => Assert.Equal<byte>([0x68, 0xCE, 0x3C, 0x80], pps),
            idr => Assert.Equal<byte>([0x65, 0x88], idr));
    }

    [Fact]
    public void TryGetFirstNalType_reads_first_payload_header()
    {
        byte[] annexB =
        [
            0x00, 0x00, 0x01, 0x41, 0x9A, 0x24
        ];

        Assert.True(H264NalUtilities.TryGetFirstNalType(annexB, out var nalType));
        Assert.Equal(1, nalType);
    }
}
