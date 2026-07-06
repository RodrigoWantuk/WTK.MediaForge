using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal sealed class ColorCorrectionEffectNode : EffectNode
{
    private readonly VulkanColorCorrectionPass _pass = new();

    public float Brightness
    {
        get => _pass.Brightness;
        set => _pass.Brightness = value;
    }

    public float Contrast
    {
        get => _pass.Contrast;
        set => _pass.Contrast = value;
    }

    public float Saturation
    {
        get => _pass.Saturation;
        set => _pass.Saturation = value;
    }

    public override bool CanApply(RenderDrawObjectSnapshot drawObject)
    {
        ConfigureFromDrawObject(drawObject);
        return _pass.CanApply(drawObject);
    }

    public override void Execute(VulkanEffectExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.DrawObject is null || !_pass.CanApply(context.DrawObject))
            return;

        _pass.Apply(context);
        context.Output.OutputTextureId = context.Input.InputTextureId;
    }

    private void ConfigureFromDrawObject(RenderDrawObjectSnapshot drawObject)
    {
        _pass.IsEnabled = false;

        foreach (var effect in drawObject.Effects.Where(static item => item.Enabled))
        {
            if (effect is not ColorCorrectionEffectSnapshot color)
                continue;

            _pass.IsEnabled = true;
            _pass.Brightness = color.Brightness;
            _pass.Contrast = color.Contrast;
            _pass.Saturation = color.Saturation;
            return;
        }
    }
}

internal sealed class SeparableBlurEffectNode : EffectNode
{
    private readonly VulkanSeparableBlurPass _pass = new();

    public float Radius
    {
        get => _pass.Radius;
        set => _pass.Radius = value;
    }

    public override bool CanApply(RenderDrawObjectSnapshot drawObject)
    {
        ConfigureFromDrawObject(drawObject);
        return _pass.CanApply(drawObject);
    }

    public override void Execute(VulkanEffectExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.DrawObject is null || !_pass.CanApply(context.DrawObject))
            return;

        _pass.ApplyHorizontal(context);
        _pass.ApplyVertical(context);
        context.Output.OutputTextureId = context.Input.InputTextureId;
    }

    private void ConfigureFromDrawObject(RenderDrawObjectSnapshot drawObject)
    {
        _pass.IsEnabled = false;

        foreach (var effect in drawObject.Effects.Where(static item => item.Enabled))
        {
            if (effect is not BlurEffectSnapshot blur)
                continue;

            _pass.IsEnabled = true;
            _pass.Radius = blur.Radius;
            return;
        }
    }
}
