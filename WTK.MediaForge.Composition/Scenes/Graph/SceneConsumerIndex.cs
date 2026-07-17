using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed class SceneConsumerIndex(SceneDependencyGraph graph)
{
    public IReadOnlyList<CanvasId> GetDirectConsumers(CanvasId canvasId) =>
        graph.GetDirectConsumers(canvasId);

    public IReadOnlyList<CanvasId> GetTransitiveConsumers(CanvasId canvasId) =>
        graph.GetTransitiveConsumers(canvasId);

    public IReadOnlyList<RenderOutputId> GetAffectedOutputs(CanvasId canvasId) =>
        graph.GetAffectedOutputs(canvasId);
}
