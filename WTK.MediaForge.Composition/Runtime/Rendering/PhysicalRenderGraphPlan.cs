namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum PhysicalRenderGraphOperationKind
{
    AcquireSourceFrame,
    RenderEffectIntermediate,
    RenderPrimitiveLayer,
    RenderCanvas,
    RenderOutputTransition,
    RenderOutput,
    FanOutRenderedOutput
}

internal sealed class PhysicalRenderGraphOperation
{
    public required PhysicalRenderGraphOperationKind Kind { get; init; }

    public required string Key { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public IReadOnlyList<string> Consumers { get; init; } = [];

    public WTK.MediaForge.Core.Identifiers.RenderOutputId? OutputId { get; init; }

    public WTK.MediaForge.Core.Identifiers.CanvasId? CanvasId { get; init; }

    public WTK.MediaForge.Core.Identifiers.CanvasId? PreviousCanvasId { get; init; }
}

internal sealed class PhysicalRenderGraphPlan
{
    public PhysicalRenderGraphPlan(IReadOnlyList<PhysicalRenderGraphOperation> operations)
    {
        Operations = operations;
        Statistics = PhysicalRenderGraphStatistics.FromOperations(operations);
    }

    public IReadOnlyList<PhysicalRenderGraphOperation> Operations { get; }

    public PhysicalRenderGraphStatistics Statistics { get; }

    public int Count(PhysicalRenderGraphOperationKind kind) =>
        Operations.Count(operation => operation.Kind == kind);
}

internal sealed record PhysicalRenderGraphStatistics(
    int SourceAcquirePasses,
    int EffectIntermediatePasses,
    int PrimitiveLayerPasses,
    int CanvasPasses,
    int OutputTransitionPasses,
    int OutputPasses,
    int FanOutGroups,
    int ReusedCanvasOutputs,
    int ReusedSourceConsumers)
{
    public static PhysicalRenderGraphStatistics FromOperations(
        IReadOnlyList<PhysicalRenderGraphOperation> operations)
    {
        var sourceConsumers = operations
            .Where(static operation => operation.Kind == PhysicalRenderGraphOperationKind.AcquireSourceFrame)
            .Sum(static operation => Math.Max(0, operation.Consumers.Count - 1));
        var effectIntermediateConsumers = operations
            .Where(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate)
            .Sum(static operation => Math.Max(0, operation.Consumers.Count - 1));
        var reusedCanvasOutputs = operations
            .Where(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas)
            .Sum(static operation => Math.Max(0, operation.Consumers.Count(IsRenderedCanvasConsumer) - 1));

        return new PhysicalRenderGraphStatistics(
            SourceAcquirePasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.AcquireSourceFrame),
            EffectIntermediatePasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate),
            PrimitiveLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderPrimitiveLayer),
            CanvasPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas),
            OutputTransitionPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutputTransition),
            OutputPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutput),
            FanOutGroups: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.FanOutRenderedOutput),
            ReusedCanvasOutputs: reusedCanvasOutputs,
            ReusedSourceConsumers: sourceConsumers + effectIntermediateConsumers);
    }

    private static bool IsRenderedCanvasConsumer(string consumer) =>
        consumer.StartsWith("output:", StringComparison.Ordinal) ||
        consumer.StartsWith("transition:", StringComparison.Ordinal);
}

internal static class PhysicalRenderGraphPlanner
{
    public static PhysicalRenderGraphPlan Create(MediaForgeRenderGraphPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var consumers = BuildConsumerMap(plan);
        var operations = new List<PhysicalRenderGraphOperation>(plan.Nodes.Count);
        foreach (var node in plan.Nodes)
        {
            operations.Add(new PhysicalRenderGraphOperation
            {
                Kind = MapKind(node.Kind),
                Key = node.Key,
                Name = node.Name,
                Dependencies = node.Dependencies,
                Consumers = consumers.TryGetValue(node.Key, out var nodeConsumers)
                    ? nodeConsumers
                    : [],
                OutputId = node.OutputId,
                CanvasId = node.CanvasId,
                PreviousCanvasId = node.PreviousCanvasId
            });
        }

        operations.AddRange(CreateFanOutOperations(plan, consumers));
        return new PhysicalRenderGraphPlan(operations);
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildConsumerMap(MediaForgeRenderGraphPlan plan)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in plan.Nodes)
        {
            foreach (var dependency in node.Dependencies)
            {
                if (!map.TryGetValue(dependency, out var nodeConsumers))
                {
                    nodeConsumers = [];
                    map.Add(dependency, nodeConsumers);
                }

                nodeConsumers.Add(node.Key);
            }
        }

        return map.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static IEnumerable<PhysicalRenderGraphOperation> CreateFanOutOperations(
        MediaForgeRenderGraphPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<string>> consumers)
    {
        foreach (var canvas in plan.Nodes.Where(static node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender))
        {
            if (!consumers.TryGetValue(canvas.Key, out var nodeConsumers))
                continue;

            var outputConsumers = nodeConsumers
                .Where(IsRenderedCanvasConsumer)
                .ToArray();
            if (outputConsumers.Length <= 1)
                continue;

            yield return new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.FanOutRenderedOutput,
                Key = $"fanout:{canvas.Key}",
                Name = $"{canvas.Name} output fanout",
                Dependencies = [canvas.Key],
                Consumers = outputConsumers,
                CanvasId = canvas.CanvasId
            };
        }
    }

    private static PhysicalRenderGraphOperationKind MapKind(MediaForgeRenderGraphNodeKind kind) =>
        kind switch
        {
            MediaForgeRenderGraphNodeKind.SourceFrame => PhysicalRenderGraphOperationKind.AcquireSourceFrame,
            MediaForgeRenderGraphNodeKind.SourceEffectChain => PhysicalRenderGraphOperationKind.RenderEffectIntermediate,
            MediaForgeRenderGraphNodeKind.PrimitiveLayer => PhysicalRenderGraphOperationKind.RenderPrimitiveLayer,
            MediaForgeRenderGraphNodeKind.CanvasRender => PhysicalRenderGraphOperationKind.RenderCanvas,
            MediaForgeRenderGraphNodeKind.OutputTransition => PhysicalRenderGraphOperationKind.RenderOutputTransition,
            MediaForgeRenderGraphNodeKind.OutputPass => PhysicalRenderGraphOperationKind.RenderOutput,
            _ => throw new NotSupportedException($"Unsupported render graph node kind '{kind}'.")
        };

    private static bool IsRenderedCanvasConsumer(string consumer) =>
        consumer.StartsWith("output:", StringComparison.Ordinal) ||
        consumer.StartsWith("transition:", StringComparison.Ordinal);
}
