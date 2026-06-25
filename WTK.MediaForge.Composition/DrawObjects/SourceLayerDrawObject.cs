using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.DrawObjects;

public sealed class SourceLayerDrawObject : MediaForgeDrawObject
{
    public SourceId SourceId { get; set; } = SourceId.New();

    public LayoutMode LayoutMode { get; set; } = LayoutMode.Fit;

    public ColorRgba LetterboxColor { get; set; } = ColorRgba.Transparent;

    public DisplayRotation? ContentRotationOverride { get; set; }
}
