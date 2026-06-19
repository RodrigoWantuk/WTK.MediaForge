namespace WTK.MediaForge.Core.Color;

public readonly struct ColorRgba : IEquatable<ColorRgba>
{
    public ColorRgba(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public float R { get; }

    public float G { get; }

    public float B { get; }

    public float A { get; }

    public static readonly ColorRgba Black = FromUnchecked(0, 0, 0, 1);

    public static readonly ColorRgba White = FromUnchecked(1, 1, 1, 1);

    public static readonly ColorRgba Transparent = FromUnchecked(0, 0, 0, 0);

    public static ColorRgba From(float r, float g, float b, float a) =>
        new(Clamp01(r), Clamp01(g), Clamp01(b), Clamp01(a));

    internal static ColorRgba FromUnchecked(float r, float g, float b, float a) =>
        new(r, g, b, a);

    public bool IsInRange() =>
        IsComponentInRange(R) &&
        IsComponentInRange(G) &&
        IsComponentInRange(B) &&
        IsComponentInRange(A);

    public bool Equals(ColorRgba other) =>
        R.Equals(other.R) &&
        G.Equals(other.G) &&
        B.Equals(other.B) &&
        A.Equals(other.A);

    public override bool Equals(object? obj) => obj is ColorRgba other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public override string ToString() => $"RGBA({R}, {G}, {B}, {A})";

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static bool IsComponentInRange(float value) => value >= 0f && value <= 1f;
}
