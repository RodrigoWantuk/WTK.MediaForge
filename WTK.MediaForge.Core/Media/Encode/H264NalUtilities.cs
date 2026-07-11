namespace WTK.MediaForge.Core.Media.Encode;

public static class H264NalUtilities
{
    public static bool ContainsValidStartCode(ReadOnlySpan<byte> data)
    {
        return TryFindStartCode(data, 0, out _, out _);
    }

    public static bool TryGetFirstNalType(ReadOnlySpan<byte> data, out int nalType)
    {
        nalType = -1;

        var units = ExtractAnnexBNalUnits(data);
        if (units.Count == 0 || units[0].Length == 0)
            return false;

        nalType = units[0][0] & 0x1F;
        return true;
    }

    public static IReadOnlyList<byte[]> ExtractAnnexBNalUnits(ReadOnlySpan<byte> data)
    {
        var units = new List<byte[]>();
        var searchOffset = 0;

        while (TryFindStartCode(data, searchOffset, out var startCodeOffset, out var startCodeLength))
        {
            var nalStart = startCodeOffset + startCodeLength;
            if (nalStart >= data.Length)
                break;

            var nextSearchOffset = nalStart;
            var nextStartCodeOffset = TryFindStartCode(
                data,
                nextSearchOffset,
                out var nextStartCode,
                out _)
                    ? nextStartCode
                    : data.Length;

            var nalEnd = TrimTrailingZeroBytes(data, nalStart, nextStartCodeOffset);
            if (nalEnd > nalStart)
                units.Add(data[nalStart..nalEnd].ToArray());

            searchOffset = nextStartCodeOffset;
        }

        return units;
    }

    private static bool TryFindStartCode(
        ReadOnlySpan<byte> data,
        int offset,
        out int startCodeOffset,
        out int startCodeLength)
    {
        startCodeOffset = -1;
        startCodeLength = 0;

        for (var index = Math.Max(0, offset); index <= data.Length - 3; index++)
        {
            if (data[index] != 0x00 || data[index + 1] != 0x00)
                continue;

            if (data[index + 2] == 0x01)
            {
                startCodeOffset = index;
                startCodeLength = 3;
                return true;
            }

            if (index + 3 < data.Length &&
                data[index + 2] == 0x00 &&
                data[index + 3] == 0x01)
            {
                startCodeOffset = index;
                startCodeLength = 4;
                return true;
            }
        }

        return false;
    }

    private static int TrimTrailingZeroBytes(
        ReadOnlySpan<byte> data,
        int start,
        int exclusiveEnd)
    {
        var end = exclusiveEnd;
        while (end > start && data[end - 1] == 0x00)
            end--;

        return end;
    }
}
