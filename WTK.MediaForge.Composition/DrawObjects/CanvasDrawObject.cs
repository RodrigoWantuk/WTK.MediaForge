using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.DrawObjects;

public sealed class CanvasDrawObject : MediaForgeDrawObject
{
    public CanvasId NestedCanvasId { get; set; }
}
