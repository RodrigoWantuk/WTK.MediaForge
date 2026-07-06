namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderGraph
{
    public RenderGraph(IReadOnlyList<RenderGraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Nodes = nodes;
        TopologicallySorted = TopologicalSort(nodes);
    }

    public IReadOnlyList<RenderGraphNode> Nodes { get; }

    public IReadOnlyList<RenderGraphNode> TopologicallySorted { get; }

    private static IReadOnlyList<RenderGraphNode> TopologicalSort(IReadOnlyList<RenderGraphNode> nodes)
    {
        var byKey = nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(node => node.Key, _ => 0, StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var dependency in node.Dependencies)
            {
                if (!byKey.ContainsKey(dependency))
                    continue;

                indegree[node.Key]++;
            }
        }

        var ready = new Queue<RenderGraphNode>(
            nodes.Where(node => indegree[node.Key] == 0).OrderBy(node => node.Key, StringComparer.Ordinal));

        var sorted = new List<RenderGraphNode>(nodes.Count);

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            sorted.Add(current);

            foreach (var dependent in nodes.Where(node => node.Dependencies.Contains(current.Key, StringComparer.Ordinal)))
            {
                indegree[dependent.Key]--;
                if (indegree[dependent.Key] == 0)
                    ready.Enqueue(dependent);
            }
        }

        if (sorted.Count != nodes.Count)
            throw new InvalidOperationException("Render graph contains a cycle.");

        return sorted;
    }
}
