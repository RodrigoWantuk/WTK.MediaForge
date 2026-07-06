using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed record SceneLayerRuntimeState
{
    public required DrawObjectId LayerId { get; init; }

    public bool IsVisible { get; init; } = true;

    public SourceId? BoundSourceId { get; init; }

    public SceneDirtyKind DirtyKind { get; init; } = SceneDirtyKind.None;
}
