using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed class SceneDependencyGraph
{
    private readonly Dictionary<CanvasId, SceneDependencyNode> _nodes;
    private readonly Dictionary<CanvasId, List<SceneDependencyEdge>> _childrenByConsumer;
    private readonly Dictionary<CanvasId, List<SceneDependencyEdge>> _consumersByChild;
    private readonly Dictionary<CanvasId, List<RenderOutputId>> _outputsByCanvas;

    internal SceneDependencyGraph(
        IEnumerable<SceneDependencyNode> nodes,
        IEnumerable<SceneDependencyEdge> edges,
        IReadOnlyDictionary<CanvasId, IReadOnlyList<RenderOutputId>> outputsByCanvas)
    {
        _nodes = nodes.ToDictionary(static node => node.CanvasId);
        _childrenByConsumer = edges
            .GroupBy(static edge => edge.ConsumerCanvasId)
            .ToDictionary(static group => group.Key, static group => group.ToList());
        _consumersByChild = edges
            .GroupBy(static edge => edge.NestedCanvasId)
            .ToDictionary(static group => group.Key, static group => group.ToList());
        _outputsByCanvas = outputsByCanvas.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToList());
    }

    public IReadOnlyCollection<CanvasId> CanvasIds => _nodes.Keys.ToArray();

    public bool Contains(CanvasId canvasId) => _nodes.ContainsKey(canvasId);

    public IReadOnlyList<CanvasId> GetDirectConsumers(CanvasId canvasId) =>
        _consumersByChild.TryGetValue(canvasId, out var edges)
            ? edges.Select(static edge => edge.ConsumerCanvasId).Distinct().ToArray()
            : Array.Empty<CanvasId>();

    public IReadOnlyList<CanvasId> GetTransitiveConsumers(CanvasId canvasId)
    {
        var result = new List<CanvasId>();
        var visited = new HashSet<CanvasId>();
        var queue = new Queue<CanvasId>(GetDirectConsumers(canvasId));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            result.Add(current);
            foreach (var consumer in GetDirectConsumers(current))
                queue.Enqueue(consumer);
        }

        return result;
    }

    public IReadOnlyList<RenderOutputId> GetAffectedOutputs(CanvasId canvasId)
    {
        var canvases = new HashSet<CanvasId> { canvasId };
        foreach (var consumer in GetTransitiveConsumers(canvasId))
            canvases.Add(consumer);

        return canvases
            .SelectMany(canvas =>
            {
                if (_outputsByCanvas.TryGetValue(canvas, out var outputs))
                    return outputs;

                return Enumerable.Empty<RenderOutputId>();
            })
            .Distinct()
            .ToArray();
    }

    public IReadOnlyList<CanvasId> GetNestedCanvases(CanvasId canvasId) =>
        _childrenByConsumer.TryGetValue(canvasId, out var edges)
            ? edges.Select(static edge => edge.NestedCanvasId).Distinct().ToArray()
            : Array.Empty<CanvasId>();
}
