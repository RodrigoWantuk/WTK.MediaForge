using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Rendering;

public sealed class EffectExecutionPlannerTests
{
    [Fact]
    public void Planner_orders_effects_and_fuses_adjacent_inline_work()
    {
        EffectStateSnapshot[] effects =
        [
            new BlurEffectSnapshot { Id = EffectId.New(), Name = "Blur", Order = 2, Radius = 4f },
            new ChromaKeyEffectSnapshot { Id = EffectId.New(), Name = "Key", Order = 1 },
            new ColorCorrectionEffectSnapshot { Id = EffectId.New(), Name = "Color", Order = 0 },
            new ColorCorrectionEffectSnapshot { Id = EffectId.New(), Name = "Disabled", Order = 3, Enabled = false }
        ];

        var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, effects);

        Assert.Collection(
            plan.OrderedEffects,
            effect => Assert.IsType<ColorCorrectionEffectSnapshot>(effect),
            effect => Assert.IsType<ChromaKeyEffectSnapshot>(effect),
            effect => Assert.IsType<BlurEffectSnapshot>(effect));
        Assert.Collection(
            plan.Passes,
            pass =>
            {
                Assert.Equal(EffectPassClass.InlineFragment, pass.PassClass);
                Assert.Equal(2, pass.Effects.Length);
                Assert.False(pass.RequiresIntermediateTarget);
            },
            pass =>
            {
                Assert.Equal(EffectPassClass.Spatial, pass.PassClass);
                Assert.Single(pass.Effects);
                Assert.True(pass.RequiresIntermediateTarget);
            });
    }

    [Fact]
    public void Fingerprint_is_semantic_and_order_sensitive()
    {
        var effect = new BlurEffectSnapshot { Id = EffectId.New(), Name = "Blur", Radius = 4f };
        var first = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, [effect]);
        var equivalent = EffectExecutionPlanner.Default.CreatePlan(
            EffectScope.Layer,
            [new BlurEffectSnapshot { Id = EffectId.New(), Name = "Renamed", Radius = 4f }]);
        var changed = EffectExecutionPlanner.Default.CreatePlan(
            EffectScope.Layer,
            [new BlurEffectSnapshot { Id = EffectId.New(), Name = "Blur", Radius = 8f }]);

        Assert.Equal(first.Fingerprint, equivalent.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }
}
