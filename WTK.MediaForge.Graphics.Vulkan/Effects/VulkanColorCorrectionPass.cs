using WTK.MediaForge.Composition.Snapshots;
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

    public void ApplySkeleton(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
    }
}
