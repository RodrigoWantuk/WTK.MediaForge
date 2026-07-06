using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal abstract class TransformEffectNode : EffectNode
{
    public abstract void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop);
}

internal sealed class TranslateEffectNode : TransformEffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) => drawObject.Enabled;

    public override void Execute(VulkanEffectExecutionContext context)
    {
        if (context.DrawObject is null)
            return;

        var transform = context.DrawObject.Transform;
        var opacity = context.DrawObject.Opacity;
        var crop = context.DrawObject.EffectiveCrop;
        ApplyTransform(transform, ref opacity, ref crop);
    }

    public override void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop)
    {
    }
}

internal sealed class RotateEffectNode : TransformEffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        drawObject.Enabled && MathF.Abs(drawObject.Transform.RotationDegrees) > 0.001f;

    public override void Execute(VulkanEffectExecutionContext context)
    {
        if (context.DrawObject is null)
            return;

        var transform = context.DrawObject.Transform;
        var opacity = context.DrawObject.Opacity;
        var crop = context.DrawObject.EffectiveCrop;
        ApplyTransform(transform, ref opacity, ref crop);
    }

    public override void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop)
    {
    }
}

internal sealed class ScaleEffectNode : TransformEffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        drawObject.Enabled && drawObject.Transform.HasPositiveSize;

    public override void Execute(VulkanEffectExecutionContext context)
    {
        if (context.DrawObject is null)
            return;

        var transform = context.DrawObject.Transform;
        var opacity = context.DrawObject.Opacity;
        var crop = context.DrawObject.EffectiveCrop;
        ApplyTransform(transform, ref opacity, ref crop);
    }

    public override void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop)
    {
    }
}

internal sealed class CropEffectNode : TransformEffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        drawObject.Enabled && !IsFullCrop(drawObject.EffectiveCrop);

    public override void Execute(VulkanEffectExecutionContext context)
    {
        if (context.DrawObject is null)
            return;

        var transform = context.DrawObject.Transform;
        var opacity = context.DrawObject.Opacity;
        var crop = context.DrawObject.EffectiveCrop;
        ApplyTransform(transform, ref opacity, ref crop);
    }

    public override void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop)
    {
    }

    private static bool IsFullCrop(NormalizedRect crop) =>
        crop.Left == 0f && crop.Top == 0f && crop.Right == 1f && crop.Bottom == 1f;
}

internal sealed class OpacityEffectNode : TransformEffectNode
{
    public override bool CanApply(RenderDrawObjectSnapshot drawObject) =>
        drawObject.Enabled && drawObject.Opacity < 0.999f;

    public override void Execute(VulkanEffectExecutionContext context)
    {
        if (context.DrawObject is null)
            return;

        var transform = context.DrawObject.Transform;
        var opacity = context.DrawObject.Opacity;
        var crop = context.DrawObject.EffectiveCrop;
        ApplyTransform(transform, ref opacity, ref crop);
    }

    public override void ApplyTransform(Transform2D transform, ref float opacity, ref NormalizedRect crop)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
    }
}

internal static class TransformEffectGraph
{
    public static IReadOnlyList<TransformEffectNode> CreateDefaultChain() =>
    [
        new TranslateEffectNode { Key = "transform.translate" },
        new RotateEffectNode { Key = "transform.rotate" },
        new ScaleEffectNode { Key = "transform.scale" },
        new CropEffectNode { Key = "transform.crop" },
        new OpacityEffectNode { Key = "transform.opacity" }
    ];
}
