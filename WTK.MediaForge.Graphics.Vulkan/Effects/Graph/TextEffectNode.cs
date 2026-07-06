using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal sealed class TextEffectNode : EffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        drawObject is RenderTextDrawObjectSnapshot { Enabled: true } text &&
        !string.IsNullOrWhiteSpace(text.Text);

    public override void Execute(VulkanEffectExecutionContext context)
    {
    }
}
