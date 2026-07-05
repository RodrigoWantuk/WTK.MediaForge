using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects;

internal sealed class VulkanSeparableBlurPass
{
    public bool IsEnabled { get; set; }

    public float Radius { get; set; }

    public bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        IsEnabled && Radius > 0f && drawObject.Enabled;

    public void ApplyHorizontalSkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
    }

    public void ApplyVerticalSkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
    }
}
