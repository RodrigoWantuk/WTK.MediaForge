using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Composition.Scenes.Editing;

namespace WTK.MediaForge.Composition.DrawObjects;

public sealed class CanvasDrawObject : MediaForgeDrawObject
{
    public CanvasId NestedCanvasId { get; set; }

    public SceneVersionBinding VersionBinding { get; set; } = SceneVersionBinding.Published;
}
