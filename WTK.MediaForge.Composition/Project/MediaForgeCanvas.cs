using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Project;

public sealed class MediaForgeCanvas
{
    public CanvasId Id { get; set; } = CanvasId.New();

    public string Name { get; set; } = string.Empty;

    public FrameSize Size { get; set; } = new(1920, 1080);

    public ColorRgba BackgroundColor { get; set; } = ColorRgba.Black;

    public List<MediaForgeDrawObject> Objects { get; set; } = [];

    public CanvasEffectStack Effects { get; set; } = [];
}
