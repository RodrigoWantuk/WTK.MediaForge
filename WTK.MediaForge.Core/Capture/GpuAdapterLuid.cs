namespace WTK.MediaForge.Core.Capture;

public readonly struct GpuAdapterLuid : IEquatable<GpuAdapterLuid>
{
    public uint LowPart { get; init; }
    public int HighPart { get; init; }

    public static GpuAdapterLuid Empty => default;

    public bool IsEmpty => LowPart == 0 && HighPart == 0;

    public bool Equals(GpuAdapterLuid other) =>
        LowPart == other.LowPart && HighPart == other.HighPart;

    public override bool Equals(object? obj) =>
        obj is GpuAdapterLuid other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LowPart, HighPart);

    public override string ToString() => $"{HighPart:X8}:{LowPart:X8}";

    public static bool operator ==(GpuAdapterLuid left, GpuAdapterLuid right) => left.Equals(right);

    public static bool operator !=(GpuAdapterLuid left, GpuAdapterLuid right) => !left.Equals(right);
}
