using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal abstract class EffectNode
{
    public required string Key { get; init; }

    public abstract bool CanApply(RenderDrawObjectSnapshot drawObject);

    public abstract void Execute(VulkanEffectExecutionContext context);
}
