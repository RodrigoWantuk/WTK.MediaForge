using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Explicit, Size = 80)]
internal struct MediaForgeSolidPushConstants
{
    [FieldOffset(0)]
    public Vector4 FillColor;

    [FieldOffset(16)]
    public Vector4 CropRect;

    [FieldOffset(32)]
    public Vector4 GeometryRect;

    [FieldOffset(48)]
    public Vector2 BoxSize;

    [FieldOffset(56)]
    public Vector2 Pivot;

    [FieldOffset(64)]
    public float Opacity;

    [FieldOffset(68)]
    public float RotationDegrees;
}
