using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Explicit, Size = 80)]
internal struct MediaForgeCanvasCompositePushConstants
{
    [FieldOffset(0)]
    public Vector4 CropRect;

    [FieldOffset(16)]
    public Vector4 GeometryRect;

    [FieldOffset(32)]
    public Vector2 BoxSize;

    [FieldOffset(40)]
    public Vector2 Pivot;

    [FieldOffset(48)]
    public float Opacity;

    [FieldOffset(52)]
    public float RotationDegrees;
}
