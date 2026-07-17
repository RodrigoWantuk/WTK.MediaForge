using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed record SceneDependencyEdge
{
    public required CanvasId ConsumerCanvasId { get; init; }

    public required CanvasId NestedCanvasId { get; init; }

    public required DrawObjectId LayerId { get; init; }
}
