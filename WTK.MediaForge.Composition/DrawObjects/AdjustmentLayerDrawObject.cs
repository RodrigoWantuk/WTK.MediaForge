using WTK.MediaForge.Composition.Effects;

namespace WTK.MediaForge.Composition.DrawObjects;

/// <summary>
/// Applies its effect stack to the already-composed portion of a canvas.
/// Adjustment layers never introduce media; their target is explicitly the
/// layers below them in canvas order.
/// </summary>
public sealed class AdjustmentLayerDrawObject : MediaForgeDrawObject
{
    public AdjustmentLayerTargetMode TargetMode { get; set; } = AdjustmentLayerTargetMode.LayersBelow;

    /// <summary>Optional coverage mask for the entire adjustment stack.</summary>
    public EffectMask? Mask { get; set; }
}

public enum AdjustmentLayerTargetMode
{
    LayersBelow = 0
}
