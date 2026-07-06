using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal sealed class EffectPassDescriptor
{
    public GpuTextureId? InputTextureId { get; set; }

    public FrameSize Size { get; init; }

    public GpuTextureId? OutputTextureId { get; set; }
}

internal sealed class VulkanEffectExecutionContext
{
    public required VulkanHeadlessDevice Device { get; init; }

    public required VulkanGpuResourcePool Pool { get; init; }

    public EffectPassDescriptor Input { get; set; } = new() { Size = new FrameSize(1, 1) };

    public EffectPassDescriptor Output { get; set; } = new() { Size = new FrameSize(1, 1) };

    public RenderDrawObjectSnapshot? DrawObject { get; set; }
}
