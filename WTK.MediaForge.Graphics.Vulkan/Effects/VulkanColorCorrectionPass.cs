using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Effects.Graph;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects;

internal sealed class VulkanColorCorrectionPass
{
    public bool IsEnabled { get; set; }

    public float Brightness { get; set; }

    public float Contrast { get; set; } = 1f;

    public float Saturation { get; set; } = 1f;

    public bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        IsEnabled && drawObject.Enabled;

    public void Apply(VulkanEffectExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void ApplySkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Apply(new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = new VulkanGpuResourcePool(device),
            Input = new EffectPassDescriptor(),
            Output = new EffectPassDescriptor()
        });
    }
}
