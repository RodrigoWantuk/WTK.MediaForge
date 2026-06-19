using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Sequential)]
internal struct PreviewPushConstants
{
    public Vector2 SourceSize;
    public Vector2 ViewportSize;
    public int Rotation;
    public int HasOverlay;
    public Vector2 OverlaySize;
}
