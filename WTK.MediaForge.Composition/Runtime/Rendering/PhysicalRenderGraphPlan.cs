namespace WTK.MediaForge.Composition.Runtime.Rendering;

using WTK.MediaForge.Composition.Snapshots;

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

    public ResolvedCanvasKey? ResolvedCanvasKey { get; init; }

    public WTK.MediaForge.Core.Identifiers.CanvasId? PreviousCanvasId { get; init; }

    public ResolvedCanvasKey? PreviousResolvedCanvasKey { get; init; }

    public WTK.MediaForge.Core.Identifiers.SourceId? SourceId { get; init; }

    public WTK.MediaForge.Core.Identifiers.DrawObjectId? DrawObjectId { get; init; }
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

    public void ValidateFor(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var operationIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < Operations.Count; index++)
        {
            var operation = Operations[index];
            if (string.IsNullOrWhiteSpace(operation.Key))
                throw new InvalidOperationException("Physical RenderGraph operations must have a stable key.");

            if (!operationIndexes.TryAdd(operation.Key, index))
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph contains duplicate operation key '{operation.Key}'.");
            }
        }

        var canvasIds = CollectCanvasIds(snapshot.Canvases);
        var resolvedCanvasKeys = CollectResolvedCanvasKeys(snapshot.Canvases);
        var expectedOutputIds = snapshot.Outputs.Select(static output => output.Id).ToHashSet();
        var plannedOutputIds = new HashSet<WTK.MediaForge.Core.Identifiers.RenderOutputId>();

        for (var index = 0; index < Operations.Count; index++)
        {
            var operation = Operations[index];
            foreach (var dependency in operation.Dependencies)
            {
                if (!operationIndexes.TryGetValue(dependency, out var dependencyIndex))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph operation '{operation.Key}' depends on missing operation '{dependency}'.");
                }

                if (dependencyIndex >= index)
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph operation '{operation.Key}' is not topologically ordered after dependency '{dependency}'.");
                }
            }

            foreach (var consumer in operation.Consumers)
            {
                if (!operationIndexes.ContainsKey(consumer))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph operation '{operation.Key}' references missing consumer '{consumer}'.");
                }
            }

            ValidateOperationIdentity(operation, canvasIds, resolvedCanvasKeys, expectedOutputIds, plannedOutputIds);
        }

        if (!plannedOutputIds.SetEquals(expectedOutputIds))
        {
            var missing = expectedOutputIds.Except(plannedOutputIds).Select(static id => id.ToString());
            var unexpected = plannedOutputIds.Except(expectedOutputIds).Select(static id => id.ToString());
            throw new InvalidOperationException(
                $"Physical RenderGraph output operations do not match the render snapshot. " +
                $"Missing=[{string.Join(", ", missing)}], Unexpected=[{string.Join(", ", unexpected)}].");
        }
    }

    private static HashSet<WTK.MediaForge.Core.Identifiers.CanvasId> CollectCanvasIds(
        IEnumerable<RenderCanvasSnapshot> rootCanvases)
    {
        var result = new HashSet<WTK.MediaForge.Core.Identifiers.CanvasId>();
        var pending = new Stack<RenderCanvasSnapshot>(rootCanvases.Reverse());
        while (pending.TryPop(out var canvas))
        {
            if (!result.Add(canvas.Id))
                continue;

            foreach (var nested in canvas.Objects
                         .OfType<RenderCanvasDrawObjectSnapshot>()
                         .Select(static drawObject => drawObject.NestedCanvas)
                         .Where(static nestedCanvas => nestedCanvas is not null))
            {
                pending.Push(nested!);
            }
        }

        return result;
    }

    private static HashSet<ResolvedCanvasKey> CollectResolvedCanvasKeys(
        IEnumerable<RenderCanvasSnapshot> rootCanvases)
    {
        var result = new HashSet<ResolvedCanvasKey>();
        var pending = new Stack<RenderCanvasSnapshot>(rootCanvases.Reverse());
        while (pending.TryPop(out var canvas))
        {
            if (!result.Add(canvas.PhysicalKey))
                continue;

            foreach (var nested in canvas.Objects
                         .OfType<RenderCanvasDrawObjectSnapshot>()
                         .Select(static drawObject => drawObject.NestedCanvas)
                         .Where(static nestedCanvas => nestedCanvas is not null))
            {
                pending.Push(nested!);
            }
        }

        return result;
    }

    private static void ValidateOperationIdentity(
        PhysicalRenderGraphOperation operation,
        IReadOnlySet<WTK.MediaForge.Core.Identifiers.CanvasId> canvasIds,
        IReadOnlySet<ResolvedCanvasKey> resolvedCanvasKeys,
        IReadOnlySet<WTK.MediaForge.Core.Identifiers.RenderOutputId> outputIds,
        ISet<WTK.MediaForge.Core.Identifiers.RenderOutputId> plannedOutputIds)
    {
        switch (operation.Kind)
        {
            case PhysicalRenderGraphOperationKind.AcquireSourceFrame when operation.SourceId is null:
                throw new InvalidOperationException(
                    $"Physical source acquisition '{operation.Key}' does not identify a source.");

            case PhysicalRenderGraphOperationKind.RenderCanvas:
                if (operation.CanvasId is not { } canvasId || !canvasIds.Contains(canvasId))
                {
                    throw new InvalidOperationException(
                        $"Physical canvas pass '{operation.Key}' references a canvas absent from the render snapshot.");
                }

                if (operation.ResolvedCanvasKey is not { } resolvedCanvasKey ||
                    !resolvedCanvasKeys.Contains(resolvedCanvasKey))
                {
                    throw new InvalidOperationException(
                        $"Physical canvas pass '{operation.Key}' references a resolved canvas absent from the render snapshot.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderOutputTransition:
                if (operation.OutputId is not { } transitionOutputId || !outputIds.Contains(transitionOutputId))
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' references an output absent from the render snapshot.");
                }

                if (operation.CanvasId is not { } currentCanvasId || !canvasIds.Contains(currentCanvasId) ||
                    operation.PreviousCanvasId is not { } previousCanvasId || !canvasIds.Contains(previousCanvasId))
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' references a canvas absent from the render snapshot.");
                }


                if (operation.ResolvedCanvasKey is not { } currentResolvedKey ||
                    !resolvedCanvasKeys.Contains(currentResolvedKey) ||
                    operation.PreviousResolvedCanvasKey is not { } previousResolvedKey ||
                    !resolvedCanvasKeys.Contains(previousResolvedKey))
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' references a resolved canvas absent from the render snapshot.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderOutput:
                if (operation.OutputId is not { } outputId || !outputIds.Contains(outputId))
                {
                    throw new InvalidOperationException(
                        $"Physical output pass '{operation.Key}' references an output absent from the render snapshot.");
                }

                if (!plannedOutputIds.Add(outputId))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph contains more than one output pass for output '{outputId}'.");
                }

                break;
        }
    }
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
                ResolvedCanvasKey = node.ResolvedCanvasKey,
                PreviousCanvasId = node.PreviousCanvasId,
                PreviousResolvedCanvasKey = node.PreviousResolvedCanvasKey,
                SourceId = node.SourceId,
                DrawObjectId = node.DrawObjectId
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
                CanvasId = canvas.CanvasId,
                ResolvedCanvasKey = canvas.ResolvedCanvasKey
            };
        }
    }

    private static PhysicalRenderGraphOperationKind MapKind(MediaForgeRenderGraphNodeKind kind) =>
        kind switch
        {
            MediaForgeRenderGraphNodeKind.SourceFrame => PhysicalRenderGraphOperationKind.AcquireSourceFrame,
            MediaForgeRenderGraphNodeKind.SourceEffectChain => PhysicalRenderGraphOperationKind.RenderEffectIntermediate,
            MediaForgeRenderGraphNodeKind.LayerEffectChain => PhysicalRenderGraphOperationKind.RenderEffectIntermediate,
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
