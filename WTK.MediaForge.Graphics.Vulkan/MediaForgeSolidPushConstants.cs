using System.Numerics;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Sequential)]
internal struct MediaForgeSolidPushConstants
{
    public Vector4 FillColor;

    public float Opacity;
}
