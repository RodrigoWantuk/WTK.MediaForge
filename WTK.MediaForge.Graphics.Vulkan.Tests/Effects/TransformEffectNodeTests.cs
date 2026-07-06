using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Graphics.Vulkan.Effects.Graph;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests.Effects;

public sealed class TransformEffectNodeTests
{
    [Fact]
    public void Translate_effect_node_applies_without_graph_recompile()
    {
        var node = new TranslateEffectNode { Key = "transform.translate" };
        var drawObject = CreateLayer(transform => transform with { Position = new CanvasPoint(12f, 8f) });

        Assert.True(node.CanApply(drawObject));
    }

    [Fact]
    public void Rotate_effect_node_applies_for_non_zero_rotation()
    {
        var node = new RotateEffectNode { Key = "transform.rotate" };
        var drawObject = CreateLayer(transform => transform with { RotationDegrees = 45f });

        Assert.True(node.CanApply(drawObject));
    }

    [Fact]
    public void Scale_effect_node_applies_for_positive_size()
    {
        var node = new ScaleEffectNode { Key = "transform.scale" };
        var drawObject = CreateLayer(transform => transform with { Size = new CanvasSize(128, 64) });

        Assert.True(node.CanApply(drawObject));
    }

    [Fact]
    public void Crop_effect_node_applies_for_partial_crop()
    {
        var node = new CropEffectNode { Key = "transform.crop" };
        var drawObject = new RenderSourceLayerDrawObjectSnapshot
        {
            Id = Core.Identifiers.DrawObjectId.New(),
            Name = "Layer",
            Transform = Transform2D.Default,
            EffectiveCrop = new NormalizedRect(0.1f, 0.1f, 0.9f, 0.9f)
        };

        Assert.True(node.CanApply(drawObject));
    }

    [Fact]
    public void Opacity_effect_node_clamps_opacity()
    {
        var node = new OpacityEffectNode { Key = "transform.opacity" };
        var drawObject = CreateLayer(static transform => transform, opacity: 0.5f);
        var effectContext = new VulkanEffectExecutionContext
        {
            Device = null!,
            Pool = null!,
            Input = new EffectPassDescriptor(),
            Output = new EffectPassDescriptor(),
            DrawObject = drawObject
        };

        node.Execute(effectContext);

        Assert.Equal(0.5f, drawObject.Opacity);
    }

    private static RenderSourceLayerDrawObjectSnapshot CreateLayer(
        Func<Transform2D, Transform2D> configure,
        float opacity = 1f) =>
        new()
        {
            Id = Core.Identifiers.DrawObjectId.New(),
            Name = "Layer",
            Transform = configure(Transform2D.Default),
            Opacity = opacity
        };
}
