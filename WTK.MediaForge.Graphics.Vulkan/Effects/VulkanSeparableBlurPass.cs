using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Effects.Graph;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects;

internal sealed class VulkanSeparableBlurPass
{
    public bool IsEnabled { get; set; }

    public float Radius { get; set; }

    public bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        IsEnabled && Radius > 0f && drawObject.Enabled;

    public void ApplyHorizontal(VulkanEffectExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void ApplyVertical(VulkanEffectExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void ApplyHorizontalSkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ApplyHorizontal(new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = new VulkanGpuResourcePool(device),
            Input = new EffectPassDescriptor(),
            Output = new EffectPassDescriptor()
        });
    }

    public void ApplyVerticalSkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ApplyVertical(new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = new VulkanGpuResourcePool(device),
            Input = new EffectPassDescriptor(),
            Output = new EffectPassDescriptor()
        });
    }
}
