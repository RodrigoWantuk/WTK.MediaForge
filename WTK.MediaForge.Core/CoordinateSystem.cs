namespace WTK.MediaForge.Core;

/// <summary>
/// Coordinate and color conventions for canvas composition.
/// See plan: canvas space uses float pixels, origin top-left, Y down.
/// </summary>
public static class CoordinateSystem
{
    /// <summary>Canvas coordinates use pixels with origin at top-left.</summary>
    public const string CanvasSpace = "Canvas pixels, origin top-left, +X right, +Y down";

    /// <summary>Texture UV uses top-left origin (D3D convention).</summary>
    public const string TextureUvSpace = "Texture UV 0..1, origin top-left";

    /// <summary>Crop is applied in logical/visible source space, not raw texture space.</summary>
    public const string CropSpace = "NormalizedRect on logical source visibility";

    /// <summary>Pipeline order: crop logical size → layout → map to full logical UV → content rotation → object transform.</summary>
    public const string CompositionPipelineOrder =
        "croppedLogicalSize → layout → logical UV → contentRotation → objectTransform → canvas → output";

    /// <summary>Model colors are sRGB straight alpha float 0..1.</summary>
    public const string ColorModel = "sRGB straight alpha, float 0..1";
}
