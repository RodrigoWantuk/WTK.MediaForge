namespace WTK.MediaForge.Composition.Effects;

public abstract class EffectStack : List<MediaForgeEffect>
{
    protected EffectStack()
    {
    }

    protected EffectStack(IEnumerable<MediaForgeEffect> effects) : base(
        effects ?? throw new ArgumentNullException(nameof(effects)))
    {
    }
}

public sealed class SourceEffectStack : EffectStack
{
    public SourceEffectStack()
    {
    }

    public SourceEffectStack(IEnumerable<MediaForgeEffect> effects) : base(effects)
    {
    }
}

public sealed class LayerEffectStack : EffectStack
{
    public LayerEffectStack()
    {
    }

    public LayerEffectStack(IEnumerable<MediaForgeEffect> effects) : base(effects)
    {
    }
}

public sealed class CanvasEffectStack : EffectStack
{
    public CanvasEffectStack()
    {
    }

    public CanvasEffectStack(IEnumerable<MediaForgeEffect> effects) : base(effects)
    {
    }
}
