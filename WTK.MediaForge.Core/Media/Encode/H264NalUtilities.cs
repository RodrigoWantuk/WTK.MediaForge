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

        if (!TryReadNextAnnexBNalUnit(data, 0, out _, out _, out var nalOffset, out var nalLength) ||
            nalLength == 0)
            return false;

        nalType = data[nalOffset] & 0x1F;
        return true;
    }

    internal static bool ContainsAnnexBNalPayload(ReadOnlySpan<byte> data) =>
        TryReadNextAnnexBNalUnit(data, 0, out _, out _, out _, out _);

    internal static bool TryReadNextAnnexBNalUnit(
        ReadOnlySpan<byte> data,
        int searchOffset,
        out int nextSearchOffset,
        out int startCodeLength,
        out int nalOffset,
        out int nalLength)
    {
        nextSearchOffset = data.Length;
        startCodeLength = 0;
        nalOffset = -1;
        nalLength = 0;

        var offset = searchOffset;
        while (TryFindStartCode(data, offset, out var startCodeOffset, out startCodeLength))
        {
            var nalStart = startCodeOffset + startCodeLength;
            if (nalStart >= data.Length)
                return false;

            var nextStartCodeOffset = TryFindStartCode(data, nalStart, out var nextStartCode, out _)
                ? nextStartCode
                : data.Length;

            var nalEnd = TrimTrailingZeroBytes(data, nalStart, nextStartCodeOffset);
            nextSearchOffset = nextStartCodeOffset;
            if (nalEnd > nalStart)
            {
                nalOffset = nalStart;
                nalLength = nalEnd - nalStart;
                return true;
            }

            offset = nextStartCodeOffset;
        }

        return false;
    }

    public static IReadOnlyList<byte[]> ExtractAnnexBNalUnits(ReadOnlySpan<byte> data)
    {
        var units = new List<byte[]>();
        var searchOffset = 0;

        while (TryReadNextAnnexBNalUnit(
            data,
            searchOffset,
            out var nextSearchOffset,
            out _,
            out var nalOffset,
            out var nalLength))
        {
            units.Add(data[nalOffset..(nalOffset + nalLength)].ToArray());
            searchOffset = nextSearchOffset;
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
