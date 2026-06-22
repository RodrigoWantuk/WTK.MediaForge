using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

[StructLayout(LayoutKind.Sequential)]
internal struct MediaForgeCanvasCompositePushConstants
{
    public float Opacity;
}
