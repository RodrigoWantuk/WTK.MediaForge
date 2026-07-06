using System.Collections.Immutable;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed record CanvasStateSnapshot
{
    public CanvasId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public FrameSize Size { get; init; }

    public ColorRgba BackgroundColor { get; init; } = ColorRgba.Black;

    public ImmutableArray<DrawObjectStateSnapshot> Objects { get; init; } = ImmutableArray<DrawObjectStateSnapshot>.Empty;
}
