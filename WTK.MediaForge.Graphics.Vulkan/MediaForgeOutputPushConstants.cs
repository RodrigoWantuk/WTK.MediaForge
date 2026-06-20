using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Sequential)]
internal struct MediaForgeOutputPushConstants
{
    public Vector2 CanvasSize;

    public Vector2 OutputSize;

    public Vector4 LetterboxColor;

    public int LayoutMode;
}
