namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderGraphExecutionResult
{
    public RenderGraphExecutionResult(
        IReadOnlyList<string> executedNodeKeys,
        IReadOnlyList<string> skippedNodeKeys,
        IReadOnlyDictionary<string, RenderGraphNodeResult> nodeResults,
        PhysicalRenderGraphPlan physicalPlan)
    {
        ExecutedNodeKeys = executedNodeKeys;
        SkippedNodeKeys = skippedNodeKeys;
        NodeResults = nodeResults;
        PhysicalPlan = physicalPlan;
    }

    public IReadOnlyList<string> ExecutedNodeKeys { get; }

    public IReadOnlyList<string> SkippedNodeKeys { get; }

    public IReadOnlyDictionary<string, RenderGraphNodeResult> NodeResults { get; }

    public PhysicalRenderGraphPlan PhysicalPlan { get; }
}

internal static class RenderGraphExecutor
{
    public static RenderGraphExecutionResult Execute(
        MediaForgeRenderGraphPlan plan,
        RenderGraphContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var graph = RenderGraphBuilder.FromPlan(plan, context.SceneSnapshot);
        var executed = new List<string>();
        var skipped = new List<string>();
        var executedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.TopologicallySorted)
        {
            if (!executedKeys.Add(node.Key))
            {
                skipped.Add(node.Key);
                continue;
            }

            var result = node.Execute(context);
            context.NodeResults[node.Key] = result;

            if (result.WasSkipped)
                skipped.Add(node.Key);
            else
                executed.Add(node.Key);
        }

        return new RenderGraphExecutionResult(
            executed,
            skipped,
            new Dictionary<string, RenderGraphNodeResult>(context.NodeResults, StringComparer.Ordinal),
            plan.PhysicalPlan);
    }
}
