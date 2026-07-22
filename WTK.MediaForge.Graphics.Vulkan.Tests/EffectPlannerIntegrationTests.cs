using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public sealed class EffectPlannerIntegrationTests
{
    [Fact]
    public void Vulkan_compositor_entry_uses_production_effect_planner()
    {
        var drawObject = new RenderSourceLayerDrawObjectSnapshot
        {
            SourceEffects = [new ColorCorrectionEffectSnapshot { Name = "Source color" }],
            Effects =
            [
                new BlurEffectSnapshot { Name = "Blur", Order = 2, Radius = 3f },
                new ColorCorrectionEffectSnapshot { Name = "Color", Order = 1 }
            ]
        };

        var plan = VulkanCompositionShaderPipelines.CreateEffectExecutionPlan(drawObject);
        var sourcePlan = VulkanCompositionShaderPipelines.CreateSourceEffectExecutionPlan(drawObject);

        Assert.Equal(EffectScope.Layer, plan.Scope);
        Assert.Equal(EffectScope.Source, sourcePlan.Scope);
        Assert.Single(sourcePlan.OrderedEffects);
        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal(EffectPassClass.InlineFragment, pass.PassClass),
            pass => Assert.Equal(EffectPassClass.Spatial, pass.PassClass));
    }
}
