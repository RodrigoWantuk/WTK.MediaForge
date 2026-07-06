namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuTextureDescriptor : IEquatable<GpuTextureDescriptor>
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public string Format { get; init; } = "R8G8B8A8_UNORM";

    public GpuTextureUsage Usage { get; init; } = GpuTextureUsage.OffscreenColor;

    public bool Recyclable { get; init; } = true;

    public bool Equals(GpuTextureDescriptor? other)
    {
        if (other is null)
            return false;

        return Width == other.Width
            && Height == other.Height
            && string.Equals(Format, other.Format, StringComparison.Ordinal)
            && Usage == other.Usage;
    }

    public override bool Equals(object? obj) => Equals(obj as GpuTextureDescriptor);

    public override int GetHashCode() =>
        HashCode.Combine(Width, Height, Format, Usage);
}
