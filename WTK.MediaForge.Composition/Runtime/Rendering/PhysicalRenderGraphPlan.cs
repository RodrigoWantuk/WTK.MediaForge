namespace WTK.MediaForge.Composition.Runtime.Rendering;

using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Snapshots;

internal enum PhysicalRenderGraphOperationKind
{
    AcquireSourceFrame = 0,
    RenderEffectIntermediate = 1,
    RenderSourceLayer = 2,
    RenderPrimitiveLayer = 3,
    RenderCanvas = 4,
    RenderCanvasEffect = 5,
    RenderAdjustmentLayer = 6,
    RenderOutputTransition = 7,
    RenderOutput = 8,
    FanOutRenderedOutput = 9,
    DispatchEncodedOutput = 10,
    RenderCanvasLayer = 11
}

internal sealed class PhysicalRenderGraphOperation
{
    public required PhysicalRenderGraphOperationKind Kind { get; init; }

    public required string Key { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public IReadOnlyList<string> Consumers { get; init; } = [];

    public WTK.MediaForge.Core.Identifiers.RenderOutputId? OutputId { get; init; }

    public WTK.MediaForge.Core.Identifiers.RenderOutputTypeId? OutputTypeId { get; init; }

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

    public void ValidateFor(RenderFrameSnapshot snapshot, bool requireCompleteSnapshotCoverage = true)
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
                if (!producer.Consumers.Contains(operation.Key, StringComparer.Ordinal))
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

                if (!Operations[operationIndexes[consumer]].Dependencies.Contains(operation.Key, StringComparer.Ordinal))
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

        if (requireCompleteSnapshotCoverage)
            ValidateSnapshotCoverage(snapshot.Canvases, Operations);
    }

    private static void ValidateSnapshotCoverage(
        IReadOnlyList<RenderCanvasSnapshot> rootCanvases,
        IReadOnlyList<PhysicalRenderGraphOperation> operations)
    {
        foreach (var canvas in EnumerateCanvases(rootCanvases))
        {
            if (!operations.Any(operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas &&
                operation.CanvasId == canvas.Id && operation.ResolvedCanvasKey == canvas.PhysicalKey))
            {
                continue;
            }

            foreach (var drawObject in canvas.Objects.Where(static drawObject => drawObject.Enabled))
            {
                switch (drawObject)
                {
                    case RenderSourceLayerDrawObjectSnapshot sourceLayer:
                        if (!EffectExecutionPlanner.Default.CreatePlan(EffectScope.Source, sourceLayer.SourceEffects).IsEmpty &&
                            !operations.Any(operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate &&
                                operation.SourceId == sourceLayer.SourceId && operation.DrawObjectId is null))
                        {
                            throw new InvalidOperationException(
                                $"Physical RenderGraph has no source-effect operation for source '{sourceLayer.SourceId}'.");
                        }

                        if (!EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, sourceLayer.Effects).IsEmpty)
                        {
                            RequireExactlyOneOperation(
                                operations,
                                operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate &&
                                    operation.CanvasId == canvas.Id &&
                                    operation.ResolvedCanvasKey == canvas.PhysicalKey &&
                                    operation.DrawObjectId == sourceLayer.Id &&
                                    operation.SourceId == sourceLayer.SourceId,
                                $"enabled layer-effect stack for source layer '{sourceLayer.Id}' on canvas '{canvas.PhysicalKey.StableValue}'");
                        }

                        RequireExactlyOneOperation(
                            operations,
                            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderSourceLayer &&
                                operation.CanvasId == canvas.Id &&
                                operation.ResolvedCanvasKey == canvas.PhysicalKey &&
                                operation.DrawObjectId == sourceLayer.Id &&
                                operation.SourceId == sourceLayer.SourceId,
                            $"enabled source layer '{sourceLayer.Id}' on canvas '{canvas.PhysicalKey.StableValue}'");
                        break;

                    case RenderTextDrawObjectSnapshot or RenderSolidDrawObjectSnapshot:
                        RequireExactlyOneOperation(
                            operations,
                            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderPrimitiveLayer &&
                                operation.CanvasId == canvas.Id &&
                                operation.ResolvedCanvasKey == canvas.PhysicalKey &&
                                operation.DrawObjectId == drawObject.Id,
                            $"enabled primitive layer '{drawObject.Id}' on canvas '{canvas.PhysicalKey.StableValue}'");
                        break;

                    case RenderAdjustmentLayerDrawObjectSnapshot adjustment when
                        !EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, adjustment.Effects).IsEmpty:
                        RequireExactlyOneOperation(
                            operations,
                            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderAdjustmentLayer &&
                                operation.CanvasId == canvas.Id &&
                                operation.ResolvedCanvasKey == canvas.PhysicalKey &&
                                operation.DrawObjectId == adjustment.Id,
                            $"enabled adjustment layer '{adjustment.Id}' on canvas '{canvas.PhysicalKey.StableValue}'");
                        break;

                    case RenderCanvasDrawObjectSnapshot nested:
                        var nestedKey = nested.NestedCanvas?.PhysicalKey ?? nested.NestedResolvedCanvasKey;
                        if (nestedKey is { IsEmpty: false } resolvedNestedKey)
                        {
                            RequireExactlyOneOperation(
                                operations,
                                operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvasLayer &&
                                    operation.CanvasId == canvas.Id &&
                                    operation.ResolvedCanvasKey == canvas.PhysicalKey &&
                                    operation.DrawObjectId == nested.Id,
                                $"enabled nested canvas layer '{nested.Id}' on canvas '{canvas.PhysicalKey.StableValue}'");

                            if (!operations.Any(operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas &&
                                operation.ResolvedCanvasKey == resolvedNestedKey))
                            {
                                throw new InvalidOperationException(
                                    $"Physical RenderGraph has no canvas operation for enabled nested canvas '{resolvedNestedKey.StableValue}'.");
                            }
                        }

                        break;
                }
            }

            if (!EffectExecutionPlanner.Default.CreatePlan(EffectScope.Canvas, canvas.Effects).IsEmpty &&
                !operations.Any(operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvasEffect &&
                    operation.CanvasId == canvas.Id && operation.ResolvedCanvasKey == canvas.PhysicalKey))
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph has no canvas-effect operation for canvas '{canvas.PhysicalKey.StableValue}'.");
            }
        }
    }

    private static void RequireExactlyOneOperation(
        IReadOnlyList<PhysicalRenderGraphOperation> operations,
        Func<PhysicalRenderGraphOperation, bool> predicate,
        string owner)
    {
        var count = operations.Count(predicate);
        if (count == 1)
            return;

        throw new InvalidOperationException(
            $"Physical RenderGraph requires exactly one operation for {owner}; found {count}.");
    }

    private static IEnumerable<RenderCanvasSnapshot> EnumerateCanvases(
        IEnumerable<RenderCanvasSnapshot> rootCanvases)
    {
        var visited = new HashSet<ResolvedCanvasKey>();
        var pending = new Stack<RenderCanvasSnapshot>(rootCanvases.Reverse());
        while (pending.TryPop(out var canvas))
        {
            if (!visited.Add(canvas.PhysicalKey))
                continue;

            yield return canvas;
            foreach (var nested in canvas.Objects
                         .OfType<RenderCanvasDrawObjectSnapshot>()
                         .Select(static drawObject => drawObject.NestedCanvas)
                         .Where(static nestedCanvas => nestedCanvas is not null))
            {
                pending.Push(nested!);
            }
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

            case PhysicalRenderGraphOperationKind.RenderCanvasLayer:
                if (operation.CanvasId is not { } canvasLayerCanvasId || !canvasIds.Contains(canvasLayerCanvasId) ||
                    operation.ResolvedCanvasKey is not { } canvasLayerResolvedCanvasKey || !resolvedCanvasKeys.Contains(canvasLayerResolvedCanvasKey) ||
                    operation.DrawObjectId is not { } canvasLayerDrawObjectId ||
                    !drawObjects.TryGetValue((canvasLayerResolvedCanvasKey, canvasLayerDrawObjectId), out var canvasLayerDrawObject) ||
                    canvasLayerDrawObject is not RenderCanvasDrawObjectSnapshot)
                {
                    throw new InvalidOperationException(
                        $"Physical nested canvas layer pass '{operation.Key}' must identify its canvas, resolved canvas and draw object.");
                }

                break;

            case PhysicalRenderGraphOperationKind.RenderSourceLayer:
                if (operation.CanvasId is not { } sourceLayerCanvasId || !canvasIds.Contains(sourceLayerCanvasId) ||
                    operation.ResolvedCanvasKey is not { } sourceLayerResolvedCanvasKey || !resolvedCanvasKeys.Contains(sourceLayerResolvedCanvasKey) ||
                    operation.DrawObjectId is not { } sourceLayerDrawObjectId ||
                    operation.SourceId is not { } sourceLayerSourceId || !sourceIds.Contains(sourceLayerSourceId) ||
                    !drawObjects.TryGetValue((sourceLayerResolvedCanvasKey, sourceLayerDrawObjectId), out var sourceLayerDrawObject) ||
                    sourceLayerDrawObject is not RenderSourceLayerDrawObjectSnapshot renderedSourceLayer ||
                    renderedSourceLayer.SourceId != sourceLayerSourceId)
                {
                    throw new InvalidOperationException(
                        $"Physical source layer pass '{operation.Key}' must identify its canvas, resolved canvas, source and draw object.");
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

            case PhysicalRenderGraphOperationKind.DispatchEncodedOutput:
                if (operation.OutputId is not { } dispatchOutputId ||
                    !outputsById.TryGetValue(dispatchOutputId, out var dispatchOutput) ||
                    operation.OutputTypeId != dispatchOutput.TypeId ||
                    !(dispatchOutput.TypeId == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.EncodedFile ||
                      dispatchOutput.TypeId == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.RecordingMp4 ||
                      dispatchOutput.TypeId == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.StreamingRtmp ||
                      dispatchOutput.TypeId == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.RemoteScene) ||
                    operation.Dependencies.Count != 1 ||
                    !operation.Dependencies[0].StartsWith("output:", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Physical encoded dispatch '{operation.Key}' must consume exactly one matching encoded output pass.");
                }

                break;

            case PhysicalRenderGraphOperationKind.FanOutRenderedOutput:
                if (operation.CanvasId is not { } fanOutCanvasId || !canvasIds.Contains(fanOutCanvasId) ||
                    operation.ResolvedCanvasKey is not { } fanOutCanvasKey ||
                    !resolvedCanvasKeys.Contains(fanOutCanvasKey) ||
                    operation.Dependencies.Count != 1 ||
                    operation.Consumers.Count < 2 ||
                    operation.Consumers.Any(static consumer =>
                        !consumer.StartsWith("output:", StringComparison.Ordinal) &&
                        !consumer.StartsWith("transition:", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Physical output fanout '{operation.Key}' must own one resolved canvas and at least two output consumers.");
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
    int SourceLayerPasses,
    int PrimitiveLayerPasses,
    int CanvasLayerPasses,
    int CanvasPasses,
    int CanvasEffectPasses,
    int AdjustmentLayerPasses,
    int OutputTransitionPasses,
    int OutputPasses,
    int EncodedOutputDispatches,
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
            .Where(static operation => operation.Kind == PhysicalRenderGraphOperationKind.FanOutRenderedOutput)
            .Sum(static operation => Math.Max(0, operation.Consumers.Count - 1));

        return new PhysicalRenderGraphStatistics(
            SourceAcquirePasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.AcquireSourceFrame),
            EffectIntermediatePasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate),
            SourceLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderSourceLayer),
            PrimitiveLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderPrimitiveLayer),
            CanvasLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvasLayer),
            CanvasPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas),
            CanvasEffectPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvasEffect),
            AdjustmentLayerPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderAdjustmentLayer),
            OutputTransitionPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutputTransition),
            OutputPasses: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutput),
            EncodedOutputDispatches: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.DispatchEncodedOutput),
            FanOutGroups: operations.Count(static operation => operation.Kind == PhysicalRenderGraphOperationKind.FanOutRenderedOutput),
            ReusedCanvasOutputs: reusedCanvasOutputs,
            ReusedSourceConsumers: sourceConsumers + effectIntermediateConsumers);
    }
}

internal static class PhysicalRenderGraphPlanner
{
    public static PhysicalRenderGraphPlan Create(MediaForgeRenderGraphPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var logicalConsumers = BuildConsumerMap(plan.Nodes.Select(static node => (node.Key, node.Dependencies)));
        var fanOutDefinitions = CreateFanOutDefinitions(plan, logicalConsumers);
        var fanOutKeysByConsumerDependency = fanOutDefinitions
            .SelectMany(static definition => definition.Consumers.Select(
                consumer => (Consumer: consumer, Dependency: definition.SourceKey, FanOutKey: definition.Key)))
            .ToDictionary(
                static entry => (entry.Consumer, entry.Dependency),
                static entry => entry.FanOutKey);
        var seeds = new List<PhysicalOperationSeed>(plan.Nodes.Count + fanOutDefinitions.Count);
        foreach (var node in plan.Nodes)
        {
            seeds.Add(PhysicalOperationSeed.FromNode(
                node,
                node.Dependencies.Select(dependency =>
                    fanOutKeysByConsumerDependency.TryGetValue((node.Key, dependency), out var fanOutKey)
                        ? fanOutKey
                        : dependency).ToArray()));

            foreach (var fanOut in fanOutDefinitions.Where(definition => definition.SourceKey == node.Key))
                seeds.Add(PhysicalOperationSeed.FromFanOut(fanOut));

            if (node.Kind == MediaForgeRenderGraphNodeKind.OutputPass && IsEncodedOutput(node.OutputTypeId))
                seeds.Add(PhysicalOperationSeed.FromEncodedDispatch(node));
        }

        var consumers = BuildConsumerMap(seeds.Select(static seed => (seed.Key, seed.Dependencies)));
        return new PhysicalRenderGraphPlan(seeds.Select(seed => seed.ToOperation(
            consumers.TryGetValue(seed.Key, out var operationConsumers) ? operationConsumers : [])).ToArray());
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildConsumerMap(
        IEnumerable<(string Key, IReadOnlyList<string> Dependencies)> nodes)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
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

    private static IReadOnlyList<FanOutDefinition> CreateFanOutDefinitions(
        MediaForgeRenderGraphPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<string>> consumers)
    {
        return plan.Nodes
            .Where(static node => node.Kind is MediaForgeRenderGraphNodeKind.CanvasRender or MediaForgeRenderGraphNodeKind.CanvasEffectChain)
            .Select(canvas => new { Canvas = canvas, Consumers = consumers.TryGetValue(canvas.Key, out var nodeConsumers) ? nodeConsumers : [] })
            .Select(candidate => new FanOutDefinition(
                candidate.Canvas.Key,
                $"fanout:{candidate.Canvas.Key}",
                $"{candidate.Canvas.Name} output fanout",
                candidate.Canvas.CanvasId,
                candidate.Canvas.ResolvedCanvasKey,
                candidate.Consumers
                .Where(IsRenderedCanvasConsumer)
                .ToArray()))
            .Where(static definition => definition.Consumers.Count > 1)
            .ToArray();
    }

    private static bool IsRenderedCanvasConsumer(string consumer) =>
        consumer.StartsWith("output:", StringComparison.Ordinal) ||
        consumer.StartsWith("transition:", StringComparison.Ordinal);

    private sealed record FanOutDefinition(
        string SourceKey,
        string Key,
        string Name,
        WTK.MediaForge.Core.Identifiers.CanvasId? CanvasId,
        ResolvedCanvasKey? ResolvedCanvasKey,
        IReadOnlyList<string> Consumers);

    private sealed record PhysicalOperationSeed(
        PhysicalRenderGraphOperationKind Kind,
        string Key,
        string Name,
        IReadOnlyList<string> Dependencies,
        WTK.MediaForge.Core.Identifiers.RenderOutputId? OutputId,
        WTK.MediaForge.Core.Identifiers.RenderOutputTypeId? OutputTypeId,
        WTK.MediaForge.Core.Identifiers.CanvasId? CanvasId,
        ResolvedCanvasKey? ResolvedCanvasKey,
        WTK.MediaForge.Core.Identifiers.CanvasId? PreviousCanvasId,
        ResolvedCanvasKey? PreviousResolvedCanvasKey,
        WTK.MediaForge.Core.Identifiers.SourceId? SourceId,
        WTK.MediaForge.Core.Identifiers.DrawObjectId? DrawObjectId)
    {
        public static PhysicalOperationSeed FromNode(MediaForgeRenderGraphNode node, IReadOnlyList<string> dependencies) =>
            new(
                MapKind(node.Kind), node.Key, node.Name, dependencies, node.OutputId, node.OutputTypeId, node.CanvasId,
                node.ResolvedCanvasKey, node.PreviousCanvasId, node.PreviousResolvedCanvasKey,
                node.SourceId, node.DrawObjectId);

        public static PhysicalOperationSeed FromFanOut(FanOutDefinition fanOut) =>
            new(
                PhysicalRenderGraphOperationKind.FanOutRenderedOutput,
                fanOut.Key,
                fanOut.Name,
                [fanOut.SourceKey],
                null,
                null,
                fanOut.CanvasId,
                fanOut.ResolvedCanvasKey,
                null,
                null,
                null,
                null);

        public static PhysicalOperationSeed FromEncodedDispatch(MediaForgeRenderGraphNode output) =>
            new(PhysicalRenderGraphOperationKind.DispatchEncodedOutput, $"encode-dispatch:{output.OutputId}",
                $"{output.Name} encoded dispatch", [output.Key], output.OutputId, output.OutputTypeId,
                output.CanvasId, output.ResolvedCanvasKey, null, null, null, null);

        public PhysicalRenderGraphOperation ToOperation(IReadOnlyList<string> consumers) =>
            new()
            {
                Kind = Kind,
                Key = Key,
                Name = Name,
                Dependencies = Dependencies,
                Consumers = consumers,
                OutputId = OutputId,
                OutputTypeId = OutputTypeId,
                CanvasId = CanvasId,
                ResolvedCanvasKey = ResolvedCanvasKey,
                PreviousCanvasId = PreviousCanvasId,
                PreviousResolvedCanvasKey = PreviousResolvedCanvasKey,
                SourceId = SourceId,
                DrawObjectId = DrawObjectId
            };
    }

    private static PhysicalRenderGraphOperationKind MapKind(MediaForgeRenderGraphNodeKind kind) =>
        kind switch
        {
            MediaForgeRenderGraphNodeKind.SourceFrame => PhysicalRenderGraphOperationKind.AcquireSourceFrame,
            MediaForgeRenderGraphNodeKind.SourceEffectChain => PhysicalRenderGraphOperationKind.RenderEffectIntermediate,
            MediaForgeRenderGraphNodeKind.LayerEffectChain => PhysicalRenderGraphOperationKind.RenderEffectIntermediate,
            MediaForgeRenderGraphNodeKind.SourceLayer => PhysicalRenderGraphOperationKind.RenderSourceLayer,
            MediaForgeRenderGraphNodeKind.PrimitiveLayer => PhysicalRenderGraphOperationKind.RenderPrimitiveLayer,
            MediaForgeRenderGraphNodeKind.CanvasLayer => PhysicalRenderGraphOperationKind.RenderCanvasLayer,
            MediaForgeRenderGraphNodeKind.CanvasRender => PhysicalRenderGraphOperationKind.RenderCanvas,
            MediaForgeRenderGraphNodeKind.CanvasEffectChain => PhysicalRenderGraphOperationKind.RenderCanvasEffect,
            MediaForgeRenderGraphNodeKind.AdjustmentLayerCheckpoint => PhysicalRenderGraphOperationKind.RenderAdjustmentLayer,
            MediaForgeRenderGraphNodeKind.OutputTransition => PhysicalRenderGraphOperationKind.RenderOutputTransition,
            MediaForgeRenderGraphNodeKind.OutputPass => PhysicalRenderGraphOperationKind.RenderOutput,
            _ => throw new NotSupportedException($"Unsupported render graph node kind '{kind}'.")
        };

    private static bool IsEncodedOutput(WTK.MediaForge.Core.Identifiers.RenderOutputTypeId? typeId) =>
        typeId is { } value && (value == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.EncodedFile ||
            value == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.RecordingMp4 ||
            value == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.StreamingRtmp ||
            value == global::WTK.MediaForge.Composition.Outputs.RenderOutputTypes.RemoteScene);

}
