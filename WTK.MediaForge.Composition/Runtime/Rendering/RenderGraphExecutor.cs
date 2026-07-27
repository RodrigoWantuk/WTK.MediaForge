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

    /// <summary>
    /// Ensures that the logical execution that produced this physical plan did not omit an
    /// operation. Production backends call this before importing resources or recording Vulkan.
    /// </summary>
    public void ValidateForProductionSubmission()
    {
        foreach (var operation in PhysicalPlan.Operations)
        {
            if (operation.Kind is PhysicalRenderGraphOperationKind.FanOutRenderedOutput or
                PhysicalRenderGraphOperationKind.DispatchEncodedOutput)
            {
                continue;
            }

            if (!NodeResults.TryGetValue(operation.Key, out var nodeResult))
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph operation '{operation.Key}' has no logical execution result.");
            }

            if (nodeResult.WasSkipped)
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph operation '{operation.Key}' was skipped and cannot be submitted." +
                    (string.IsNullOrWhiteSpace(nodeResult.FailureReason) ? string.Empty : $" {nodeResult.FailureReason}"));
            }
        }
    }
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
