using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

/// <summary>
/// Push constants for mf.source.layer fragment shader (skeleton).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MediaForgeLayerPushConstants
{
    public Vector4 CropRect;
    public Vector2 LogicalSize;
    public Vector2 BoxSize;
    public Vector2 Pivot;
    public float Opacity;
    public int LayoutMode;
    public int ContentRotation;
}
