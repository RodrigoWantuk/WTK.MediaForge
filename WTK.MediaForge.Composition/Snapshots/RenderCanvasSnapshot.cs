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

    public ResolvedCanvasKey ResolvedKey { get; init; }

    public ResolvedCanvasKey PhysicalKey =>
        ResolvedKey.IsEmpty ? ResolvedCanvasKey.Unversioned(Id) : ResolvedKey;

    /// <summary>
    /// Canvas-wide effects, executed only after all source, primitive and
    /// nested-canvas layers have been composed into this canvas target.
    /// </summary>
    public ImmutableArray<EffectStateSnapshot> Effects { get; init; } =
        ImmutableArray<EffectStateSnapshot>.Empty;

    public ImmutableArray<RenderDrawObjectSnapshot> Objects { get; init; } =
        ImmutableArray<RenderDrawObjectSnapshot>.Empty;
}
