using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanGpuTextureFactory : IGpuTextureFactory
{
    private readonly VulkanHeadlessDevice _device;

    public VulkanGpuTextureFactory(VulkanHeadlessDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public int CreateCount { get; private set; }

    public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        CreateCount++;

        var target = new VulkanOffscreenRenderTarget(
            _device,
            new FrameSize((uint)descriptor.Width, (uint)descriptor.Height));

        return new VulkanOffscreenPhysicalTexture(target);
    }
}
