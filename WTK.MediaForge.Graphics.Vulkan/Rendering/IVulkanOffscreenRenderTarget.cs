using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal interface IVulkanOffscreenRenderTarget : IDisposable
{
    FrameSize Size { get; }

    void Resize(FrameSize newSize);
}
