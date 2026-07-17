using System.Collections.Immutable;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed class RenderCanvasSnapshot
{
    public CanvasId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public FrameSize Size { get; init; }

    public ColorRgba BackgroundColor { get; init; } = ColorRgba.Black;

    public SceneVersionId? VersionId { get; init; }

    public ImmutableArray<RenderDrawObjectSnapshot> Objects { get; init; } =
        ImmutableArray<RenderDrawObjectSnapshot>.Empty;
}
