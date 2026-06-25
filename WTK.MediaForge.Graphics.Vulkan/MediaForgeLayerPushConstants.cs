using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

/// <summary>
/// Push constants for mf.source.layer fragment shader.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 112)]
internal struct MediaForgeLayerPushConstants
{
    [FieldOffset(0)]
    public Vector4 CropRect;

    [FieldOffset(16)]
    public Vector4 ChromaKeyColor;

    [FieldOffset(32)]
    public Vector4 ChromaKeyParameters;

    [FieldOffset(48)]
    public Vector2 LogicalSize;

    [FieldOffset(56)]
    public Vector2 BoxSize;

    [FieldOffset(64)]
    public Vector2 Pivot;

    [FieldOffset(72)]
    public float Opacity;

    [FieldOffset(76)]
    public int LayoutMode;

    [FieldOffset(80)]
    public int ContentRotation;

    [FieldOffset(96)]
    public Vector4 LetterboxColor;
}
