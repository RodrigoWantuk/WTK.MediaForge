using System.Collections.Immutable;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed record ResolvedOutputCanvasStateSnapshot
{
    public required CanvasStateSnapshot RootCanvas { get; init; }

    public required SceneVersionId RootVersionId { get; init; }

    public required SceneVersionBinding Binding { get; init; }

    public ImmutableArray<CanvasStateSnapshot> Canvases { get; init; } = [];

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CanvasVersionIds { get; init; } =
        new Dictionary<CanvasId, SceneVersionId>();

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CanvasVersionSnapshots { get; init; } =
        new Dictionary<SceneVersionId, CanvasStateSnapshot>();
}
