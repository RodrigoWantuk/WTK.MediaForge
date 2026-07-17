using System.Collections.Immutable;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed record ProjectStateSnapshot
{
    public long Version { get; init; }

    public ImmutableArray<CanvasStateSnapshot> Canvases { get; init; } = ImmutableArray<CanvasStateSnapshot>.Empty;

    public ImmutableArray<RenderOutputStateSnapshot> Outputs { get; init; } = ImmutableArray<RenderOutputStateSnapshot>.Empty;

    public ImmutableArray<SourceDefinitionSnapshot> Sources { get; init; } = ImmutableArray<SourceDefinitionSnapshot>.Empty;

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CanvasVersionIds { get; init; } =
        new Dictionary<CanvasId, SceneVersionId>();

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CanvasVersionSnapshots { get; init; } =
        new Dictionary<SceneVersionId, CanvasStateSnapshot>();
}
