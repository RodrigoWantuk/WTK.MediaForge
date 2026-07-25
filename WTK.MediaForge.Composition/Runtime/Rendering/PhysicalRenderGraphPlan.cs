namespace WTK.MediaForge.Composition.Runtime.Rendering;

using WTK.MediaForge.Composition.Snapshots;

internal enum PhysicalRenderGraphOperationKind
{
    AcquireSourceFrame,
    RenderEffectIntermediate,
    RenderPrimitiveLayer,
    RenderCanvas,
    RenderCanvasEffect,
    RenderAdjustmentLayer,
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
        var drawObjects = CollectDrawObjects(snapshot.Canvases);
        var sourceIds = CollectSourceIds(snapshot.Canvases);
        var outputsById = snapshot.Outputs.ToDictionary(static output => output.Id);
        var expectedOutputIds = outputsById.Keys.ToHashSet();
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

                var producer = Operations[dependencyIndex];
                if (operation.Kind != PhysicalRenderGraphOperationKind.FanOutRenderedOutput &&
                    !producer.Consumers.Contains(operation.Key, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph dependency '{dependency}' does not declare '{operation.Key}' as a consumer.");
                }
            }

            foreach (var consumer in operation.Consumers)
            {
                if (!operationIndexes.ContainsKey(consumer))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph operation '{operation.Key}' references missing consumer '{consumer}'.");
                }

                if (operation.Kind != PhysicalRenderGraphOperationKind.FanOutRenderedOutput &&
                    !Operations[operationIndexes[consumer]].Dependencies.Contains(operation.Key, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph consumer '{consumer}' does not declare '{operation.Key}' as a dependency.");
                }
            }

            ValidateOperationIdentity(
                operation,
                canvasIds,
                resolvedCanvasKeys,
                drawObjects,
                sourceIds,
                outputsById,
                snapshot.Canvases,
                plannedOutputIds);
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

    private static Dictionary<(ResolvedCanvasKey CanvasKey, WTK.MediaForge.Core.Identifiers.DrawObjectId DrawObjectId), RenderDrawObjectSnapshot> CollectDrawObjects(
        IEnumerable<RenderCanvasSnapshot> rootCanvases)
    {
        var result = new Dictionary<(ResolvedCanvasKey, WTK.MediaForge.Core.Identifiers.DrawObjectId), RenderDrawObjectSnapshot>();
        var pending = new Stack<RenderCanvasSnapshot>(rootCanvases.Reverse());
        while (pending.TryPop(out var canvas))
        {
            foreach (var drawObject in canvas.Objects)
            {
                result[(canvas.PhysicalKey, drawObject.Id)] = drawObject;
                if (drawObject is RenderCanvasDrawObjectSnapshot { NestedCanvas: { } nested })
                    pending.Push(nested);
            }
        }

        return result;
    }

    private static HashSet<WTK.MediaForge.Core.Identifiers.SourceId> CollectSourceIds(
        IEnumerable<RenderCanvasSnapshot> rootCanvases)
    {
        var result = new HashSet<WTK.MediaForge.Core.Identifiers.SourceId>();
        var pending = new Stack<RenderCanvasSnapshot>(rootCanvases.Reverse());
        while (pending.TryPop(out var canvas))
        {
            foreach (var drawObject in canvas.Objects)
            {
                if (drawObject is RenderSourceLayerDrawObjectSnapshot sourceLayer)
                    result.Add(sourceLayer.SourceId);
                else if (drawObject is RenderCanvasDrawObjectSnapshot { NestedCanvas: { } nested })
                    pending.Push(nested);
            }
        }

        return result;
    }

    private static void ValidateOperationIdentity(
        PhysicalRenderGraphOperation operation,
        IReadOnlySet<WTK.MediaForge.Core.Identifiers.CanvasId> canvasIds,
        IReadOnlySet<ResolvedCanvasKey> resolvedCanvasKeys,
        IReadOnlyDictionary<(ResolvedCanvasKey CanvasKey, WTK.MediaForge.Core.Identifiers.DrawObjectId DrawObjectId), RenderDrawObjectSnapshot> drawObjects,
        IReadOnlySet<WTK.MediaForge.Core.Identifiers.SourceId> sourceIds,
        IReadOnlyDictionary<WTK.MediaForge.Core.Identifiers.RenderOutputId, RenderOutputStateSnapshot> outputsById,
        IReadOnlyList<RenderCanvasSnapshot> rootCanvases,
        ISet<WTK.MediaForge.Core.Identifiers.RenderOutputId> plannedOutputIds)
    {
        switch (operation.Kind)
        {
            case PhysicalRenderGraphOperationKind.AcquireSourceFrame:
                if (operation.SourceId is not { } acquiredSourceId || !sourceIds.Contains(acquiredSourceId))
                {
                    throw new InvalidOperationException(
                        $"Physical source acquisition '{operation.Key}' references a source absent from the render snapshot.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderCanvas:
            case PhysicalRenderGraphOperationKind.RenderCanvasEffect:
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

            case PhysicalRenderGraphOperationKind.RenderAdjustmentLayer:
                if (operation.CanvasId is not { } adjustmentCanvasId || !canvasIds.Contains(adjustmentCanvasId) ||
                    operation.ResolvedCanvasKey is not { } adjustmentResolvedCanvasKey ||
                    !resolvedCanvasKeys.Contains(adjustmentResolvedCanvasKey) ||
                    operation.DrawObjectId is not { } adjustmentDrawObjectId ||
                    !drawObjects.TryGetValue((adjustmentResolvedCanvasKey, adjustmentDrawObjectId), out var adjustmentDrawObject) ||
                    adjustmentDrawObject is not RenderAdjustmentLayerDrawObjectSnapshot)
                {
                    throw new InvalidOperationException(
                        $"Physical adjustment pass '{operation.Key}' must identify its canvas, resolved canvas and adjustment layer.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderPrimitiveLayer:
                if (operation.CanvasId is not { } primitiveCanvasId || !canvasIds.Contains(primitiveCanvasId) ||
                    operation.ResolvedCanvasKey is not { } primitiveResolvedCanvasKey || !resolvedCanvasKeys.Contains(primitiveResolvedCanvasKey) ||
                    operation.DrawObjectId is not { } primitiveDrawObjectId ||
                    !drawObjects.TryGetValue((primitiveResolvedCanvasKey, primitiveDrawObjectId), out var primitiveDrawObject) ||
                    primitiveDrawObject is not RenderTextDrawObjectSnapshot and not RenderSolidDrawObjectSnapshot)
                {
                    throw new InvalidOperationException(
                        $"Physical primitive pass '{operation.Key}' must identify its canvas, resolved canvas and draw object.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderEffectIntermediate:
                if (operation.SourceId is not { } effectSourceId || !sourceIds.Contains(effectSourceId) ||
                    operation.DrawObjectId is { } effectDrawObjectId &&
                    (operation.ResolvedCanvasKey is not { } effectCanvasKey ||
                     !drawObjects.TryGetValue((effectCanvasKey, effectDrawObjectId), out var effectDrawObject) ||
                     effectDrawObject is not RenderSourceLayerDrawObjectSnapshot sourceLayer || sourceLayer.SourceId != effectSourceId))
                {
                    throw new InvalidOperationException(
                        $"Physical effect pass '{operation.Key}' references a source or draw object absent from the render snapshot.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderOutputTransition:
                if (operation.OutputId is not { } transitionOutputId ||
                    !outputsById.TryGetValue(transitionOutputId, out var transitionOutput))
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' references an output absent from the render snapshot.");
                }

                if (transitionOutput.RouteTransitionKind == WTK.MediaForge.Composition.Outputs.OutputRouteTransitionKind.Cut ||
                    transitionOutput.PreviousCanvasId is not { } expectedPreviousCanvasId ||
                    operation.CanvasId != transitionOutput.CanvasId ||
                    operation.PreviousCanvasId != expectedPreviousCanvasId)
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' must identify the current and previous canvases declared by output '{transitionOutputId}'.");
                }

                var expectedCurrentResolvedKey = ResolveOutputCanvasKey(transitionOutput, rootCanvases);
                var expectedPreviousResolvedKey = ResolvePreviousCanvasKey(
                    transitionOutput,
                    expectedPreviousCanvasId,
                    rootCanvases);
                if (operation.ResolvedCanvasKey != expectedCurrentResolvedKey ||
                    operation.PreviousResolvedCanvasKey != expectedPreviousResolvedKey ||
                    !resolvedCanvasKeys.Contains(expectedCurrentResolvedKey) ||
                    !resolvedCanvasKeys.Contains(expectedPreviousResolvedKey))
                {
                    throw new InvalidOperationException(
                        $"Physical transition pass '{operation.Key}' must identify the resolved canvases declared by output '{transitionOutputId}'.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderOutput:
                if (operation.OutputId is not { } outputId ||
                    !outputsById.TryGetValue(outputId, out var output))
                {
                    throw new InvalidOperationException(
                        $"Physical output pass '{operation.Key}' references an output absent from the render snapshot.");
                }

                var expectedOutputCanvasKey = ResolveOutputCanvasKey(output, rootCanvases);
                if (operation.CanvasId != output.CanvasId ||
                    operation.ResolvedCanvasKey != expectedOutputCanvasKey ||
                    !resolvedCanvasKeys.Contains(expectedOutputCanvasKey))
                {
                    throw new InvalidOperationException(
                        $"Physical output pass '{operation.Key}' must identify the canvas and resolved canvas declared by output '{outputId}'.");
                }

                if (!plannedOutputIds.Add(outputId))
                {
                    throw new InvalidOperationException(
                        $"Physical RenderGraph contains more than one output pass for output '{outputId}'.");
                }

                break;
        }
    }

    private static ResolvedCanvasKey ResolveOutputCanvasKey(
        RenderOutputStateSnapshot output,
        IReadOnlyList<RenderCanvasSnapshot> rootCanvases)
    {
        if (!output.ResolvedCanvasKey.IsEmpty)
            return output.ResolvedCanvasKey;

        return ResolveCanvasKey(output.CanvasId, rootCanvases, output.Name, "output");
    }

    private static ResolvedCanvasKey ResolvePreviousCanvasKey(
        RenderOutputStateSnapshot output,
        WTK.MediaForge.Core.Identifiers.CanvasId previousCanvasId,
        IReadOnlyList<RenderCanvasSnapshot> rootCanvases)
    {
        if (output.PreviousResolvedCanvasKey is { IsEmpty: false } resolvedCanvasKey)
            return resolvedCanvasKey;

        return ResolveCanvasKey(previousCanvasId, rootCanvases, output.Name, "output transition");
    }

    private static ResolvedCanvasKey ResolveCanvasKey(
        WTK.MediaForge.Core.Identifiers.CanvasId canvasId,
        IReadOnlyList<RenderCanvasSnapshot> rootCanvases,
        string outputName,
        string operation)
    {
        var candidates = rootCanvases
            .Where(canvas => canvas.Id == canvasId)
            .Select(static canvas => canvas.PhysicalKey)
            .Distinct()
            .ToArray();
        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException(
                $"{operation} for '{outputName}' does not identify one resolved canvas revision.");
    }
}

internal sealed record PhysicalRenderGraphStatistics(
    int SourceAcquirePasses,
    int EffectIntermediatePasses,
    int PrimitiveLayerPasses,
    int CanvasPasses,
    int CanvasEffectPasses,
    int AdjustmentLayerPasses,
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
            CanvasEffectPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvasEffect),
            AdjustmentLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderAdjustmentLayer),
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
        foreach (var canvas in plan.Nodes.Where(static node =>
                     node.Kind is MediaForgeRenderGraphNodeKind.CanvasRender or MediaForgeRenderGraphNodeKind.CanvasEffectChain))
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
            MediaForgeRenderGraphNodeKind.CanvasEffectChain => PhysicalRenderGraphOperationKind.RenderCanvasEffect,
            MediaForgeRenderGraphNodeKind.AdjustmentLayerCheckpoint => PhysicalRenderGraphOperationKind.RenderAdjustmentLayer,
            MediaForgeRenderGraphNodeKind.OutputTransition => PhysicalRenderGraphOperationKind.RenderOutputTransition,
            MediaForgeRenderGraphNodeKind.OutputPass => PhysicalRenderGraphOperationKind.RenderOutput,
            _ => throw new NotSupportedException($"Unsupported render graph node kind '{kind}'.")
        };

    private static bool IsRenderedCanvasConsumer(string consumer) =>
        consumer.StartsWith("output:", StringComparison.Ordinal) ||
        consumer.StartsWith("transition:", StringComparison.Ordinal);
}
