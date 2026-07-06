namespace WTK.MediaForge.Core.Media.Encode;

public static class H264NalUtilities
{
    public static bool ContainsValidStartCode(ReadOnlySpan<byte> data)
    {
        for (var index = 0; index <= data.Length - 3; index++)
        {
            if (data[index] == 0x00 &&
                data[index + 1] == 0x00 &&
                (data[index + 2] == 0x01 ||
                 (data[index + 2] == 0x00 && index + 3 < data.Length && data[index + 3] == 0x01)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetFirstNalType(ReadOnlySpan<byte> data, out int nalType)
    {
        nalType = -1;

        for (var index = 0; index <= data.Length - 4; index++)
        {
            if (data[index] != 0x00 || data[index + 1] != 0x00)
                continue;

            var startCodeLength = data[index + 2] == 0x01 ? 3 : 0;
            if (startCodeLength == 0 &&
                index + 3 < data.Length &&
                data[index + 2] == 0x00 &&
                data[index + 3] == 0x01)
            {
                startCodeLength = 4;
            }

            if (startCodeLength == 0)
                continue;

            var nalHeaderIndex = index + startCodeLength;
            if (nalHeaderIndex >= data.Length)
                return false;

            nalType = data[nalHeaderIndex] & 0x1F;
            return true;
        }

        return false;
    }
}
