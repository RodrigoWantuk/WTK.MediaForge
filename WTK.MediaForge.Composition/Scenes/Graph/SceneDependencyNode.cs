using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed record SceneDependencyNode
{
    public required CanvasId CanvasId { get; init; }

    public required string Name { get; init; }
}
