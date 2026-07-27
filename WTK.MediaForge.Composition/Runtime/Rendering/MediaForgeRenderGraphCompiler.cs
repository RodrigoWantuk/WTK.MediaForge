using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal static class MediaForgeRenderGraphCompiler
{
    public static MediaForgeRenderGraphPlan Compile(MediaForgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Compile(ProjectStateSnapshotFactory.CreateImmutableSnapshot(project));
    }

    public static MediaForgeRenderGraphPlan Compile(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        var builder = new Builder(projectState);
        foreach (var output in projectState.Outputs)
            builder.AddOutput(output);

        return new MediaForgeRenderGraphPlan(builder.Nodes);
    }

    public static MediaForgeRenderGraphPlan Compile(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new RenderSnapshotBuilder(snapshot);
        foreach (var output in snapshot.Outputs)
            builder.AddOutput(output);

        return new MediaForgeRenderGraphPlan(builder.Nodes);
    }

    private sealed class Builder(ProjectStateSnapshot projectState)
    {
        private readonly Dictionary<string, MediaForgeRenderGraphNode> _nodes = new(StringComparer.Ordinal);

        public IReadOnlyList<MediaForgeRenderGraphNode> Nodes => _nodes.Values.ToList();

        public string AddOutput(RenderOutputStateSnapshot output)
        {
            var canvasKey = AddCanvas(output.CanvasId, output.SceneVersionBinding, output.ColorSpace);
            var dependency = canvasKey;
            if (output.RouteTransitionKind != OutputRouteTransitionKind.Cut &&
                output.PreviousCanvasId is { } previousCanvasId)
            {
                var previousCanvasKey = AddCanvas(previousCanvasId, SceneVersionBinding.Published, output.ColorSpace);
                dependency = AddNode(
                    MediaForgeRenderGraphNodeKind.OutputTransition,
                    $"transition:{output.Id}:previous:{previousCanvasId}:current:{output.CanvasId}:progress:{output.RouteTransitionProgress:0.####}",
                    $"{output.Name} route transition",
                    [previousCanvasKey, canvasKey],
                    outputId: output.Id,
                    canvasId: output.CanvasId,
                    previousCanvasId: previousCanvasId);
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.OutputPass,
                $"output:{output.Id}:canvas:{output.CanvasId}:binding:{ResolveCanvasVersionKey(output.CanvasId, output.SceneVersionBinding)}:size:{output.OutputSize.Width}x{output.OutputSize.Height}:layout:{output.CanvasLayoutMode}:letterbox:{output.LetterboxColor.R:R},{output.LetterboxColor.G:R},{output.LetterboxColor.B:R},{output.LetterboxColor.A:R}:color-space:{output.ColorSpace}",
                output.Name,
                [dependency],
                outputId: output.Id,
                outputTypeId: output.TypeId,
                canvasId: output.CanvasId);
        }

        private string AddCanvas(
            Core.Identifiers.CanvasId canvasId,
            SceneVersionBinding binding,
            RenderColorSpace colorSpace)
        {
            var canvas = projectState.Canvases.FirstOrDefault(candidate => candidate.Id == canvasId);
            if (canvas is null)
                return $"missing-canvas:{canvasId}";

            var versionKey = ResolveCanvasVersionKey(canvasId, binding);
            var dependencies = new List<string>();
            foreach (var drawObject in canvas.Objects.Where(static item => item.Enabled))
            {
                switch (drawObject)
                {
                    case SourceLayerDrawObjectSnapshot sourceLayer:
                        var sourceKey = AddNode(
                            MediaForgeRenderGraphNodeKind.SourceFrame,
                            $"source:{sourceLayer.SourceId}",
                            sourceLayer.Name,
                            sourceId: sourceLayer.SourceId);

                        var dependency = sourceKey;
                        var sourceDefinition = projectState.Sources.FirstOrDefault(
                            candidate => candidate.Id == sourceLayer.SourceId);
                        var sourcePlan = EffectExecutionPlanner.Default.CreatePlan(
                            EffectScope.Source,
                            sourceDefinition?.Effects ?? []);
                        if (!sourcePlan.IsEmpty)
                        {
                            dependency = AddNode(
                                MediaForgeRenderGraphNodeKind.SourceEffectChain,
                                CreateSourceEffectKey(
                                    sourceLayer.SourceId,
                                    frameNumber: -1,
                                    pixelFormat: "PROJECT_SOURCE",
                                    resolution: canvas.Size,
                                    colorSpace,
                                    sourcePlan.Fingerprint),
                                sourceLayer.Name,
                                [sourceKey],
                                sourceId: sourceLayer.SourceId);
                        }

                        var enabledEffects = GetEnabledEffects(sourceLayer);
                        if (enabledEffects.Count > 0)
                        {
                            dependencies.Add(AddNode(
                                MediaForgeRenderGraphNodeKind.LayerEffectChain,
                                CreateLayerEffectKey(canvas, sourceLayer, enabledEffects),
                                sourceLayer.Name,
                                [dependency],
                                canvasId: canvas.Id,
                                sourceId: sourceLayer.SourceId,
                                drawObjectId: sourceLayer.Id));
                        }
                        else
                        {
                            dependencies.Add(dependency);
                        }

                        break;

                    case CanvasDrawObjectSnapshot nested:
                        dependencies.Add(AddCanvas(nested.NestedCanvasId, nested.VersionBinding, colorSpace));
                        break;

                    case AdjustmentLayerDrawObjectSnapshot adjustment:
                        AddAdjustmentLayerCheckpoint(canvas, adjustment, colorSpace, dependencies);
                        break;

                    case TextDrawObjectSnapshot:
                    case SolidDrawObjectSnapshot:
                        dependencies.Add(AddNode(
                            MediaForgeRenderGraphNodeKind.PrimitiveLayer,
                            $"primitive:{drawObject.Id}:{HashPrimitive(drawObject)}",
                            drawObject.Name,
                            canvasId: canvas.Id,
                            drawObjectId: drawObject.Id));
                        break;
                }
            }

            var canvasNode = AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.Id}:version:{versionKey}:size:{canvas.Size.Width}x{canvas.Size.Height}:color-space:{colorSpace}:content:{HashCanvas(canvas)}",
                canvas.Name,
                dependencies,
                canvasId: canvas.Id);

            return AddCanvasEffectChain(canvas, colorSpace, canvasNode);
        }

        private string AddCanvasEffectChain(
            CanvasStateSnapshot canvas,
            RenderColorSpace colorSpace,
            string canvasNode)
        {
            var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Canvas, canvas.Effects);
            return plan.IsEmpty
                ? canvasNode
                : AddNode(
                    MediaForgeRenderGraphNodeKind.CanvasEffectChain,
                    $"canvas-effect:{canvas.Id}:size:{canvas.Size.Width}x{canvas.Size.Height}:color-space:{colorSpace}:stack:{plan.Fingerprint.Value}",
                    canvas.Name,
                    [canvasNode],
                    canvasId: canvas.Id);
        }

        private string ResolveCanvasVersionKey(Core.Identifiers.CanvasId canvasId, SceneVersionBinding binding)
        {
            binding.Validate();
            return binding.Kind switch
            {
                SceneVersionBindingKind.Published => projectState.CanvasVersionIds.TryGetValue(canvasId, out var version)
                    ? $"published:{version.Value}"
                    : "published:unversioned",
                SceneVersionBindingKind.Draft => $"draft:{binding.DraftSessionId!.Value.Value}",
                SceneVersionBindingKind.ExplicitVersion => $"explicit:{binding.ExplicitVersionId!.Value.Value}",
                _ => throw new InvalidOperationException($"Unsupported scene binding kind '{binding.Kind}'.")
            };
        }

        private string AddNode(
            MediaForgeRenderGraphNodeKind kind,
            string key,
            string name,
            IReadOnlyList<string>? dependencies = null,
            Core.Identifiers.RenderOutputId? outputId = null,
            Core.Identifiers.RenderOutputTypeId? outputTypeId = null,
            Core.Identifiers.CanvasId? canvasId = null,
            Core.Identifiers.CanvasId? previousCanvasId = null,
            Core.Identifiers.SourceId? sourceId = null,
            Core.Identifiers.DrawObjectId? drawObjectId = null)
        {
            if (_nodes.TryGetValue(key, out _))
                return key;

            _nodes.Add(
                key,
                new MediaForgeRenderGraphNode
                {
                    Kind = kind,
                    Key = key,
                    Name = name,
                    Dependencies = dependencies ?? [],
                    OutputId = outputId,
                    OutputTypeId = outputTypeId,
                    CanvasId = canvasId,
                    PreviousCanvasId = previousCanvasId,
                    SourceId = sourceId,
                    DrawObjectId = drawObjectId
                });
            return key;
        }

        private void AddAdjustmentLayerCheckpoint(
            CanvasStateSnapshot canvas,
            AdjustmentLayerDrawObjectSnapshot adjustment,
            RenderColorSpace colorSpace,
            List<string> dependencies)
        {
            var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, adjustment.Effects);
            if (plan.IsEmpty)
                return;

            var checkpoint = AddNode(
                MediaForgeRenderGraphNodeKind.AdjustmentLayerCheckpoint,
                $"adjustment:{canvas.Id}:draw:{adjustment.Id}:color-space:{colorSpace}:state:{DrawObjectVisualStateFingerprint.Create(adjustment)}:effects:{plan.Fingerprint.Value}",
                adjustment.Name,
                dependencies.ToArray(),
                canvasId: canvas.Id,
                drawObjectId: adjustment.Id);
            dependencies.Clear();
            dependencies.Add(checkpoint);
        }
    }

    private sealed class RenderSnapshotBuilder(RenderFrameSnapshot snapshot)
    {
        private readonly Dictionary<string, MediaForgeRenderGraphNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<ResolvedCanvasKey, RenderCanvasSnapshot> _canvasLookup =
            snapshot.Canvases.ToDictionary(static canvas => canvas.PhysicalKey);

        public IReadOnlyList<MediaForgeRenderGraphNode> Nodes => _nodes.Values.ToList();

        public string AddOutput(RenderOutputStateSnapshot output)
        {
            var currentResolvedKey = ResolveOutputCanvasKey(output);
            var canvasKey = AddCanvas(currentResolvedKey, output.ColorSpace);
            var dependency = canvasKey;

            if (output.RouteTransitionKind != OutputRouteTransitionKind.Cut &&
                output.PreviousCanvasId is { } previousCanvasId)
            {
                var previousResolvedCanvasKey = ResolvePreviousCanvasKey(output, previousCanvasId);
                var previousCanvasKey = AddCanvas(previousResolvedCanvasKey, output.ColorSpace);
                dependency = AddNode(
                    MediaForgeRenderGraphNodeKind.OutputTransition,
                    $"transition:{output.Id}:previous:{previousResolvedCanvasKey.StableValue}:current:{currentResolvedKey.StableValue}:progress:{output.RouteTransitionProgress:0.####}",
                    $"{output.Name} route transition",
                    [previousCanvasKey, canvasKey],
                    outputId: output.Id,
                    canvasId: output.CanvasId,
                    resolvedCanvasKey: currentResolvedKey,
                    previousCanvasId: previousCanvasId,
                    previousResolvedCanvasKey: previousResolvedCanvasKey);
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.OutputPass,
                $"output:{output.Id}:canvas:{currentResolvedKey.StableValue}:size:{output.OutputSize.Width}x{output.OutputSize.Height}:layout:{output.CanvasLayoutMode}:letterbox:{output.LetterboxColor.R:R},{output.LetterboxColor.G:R},{output.LetterboxColor.B:R},{output.LetterboxColor.A:R}:color-space:{output.ColorSpace}",
                output.Name,
                [dependency],
                outputId: output.Id,
                outputTypeId: output.TypeId,
                canvasId: output.CanvasId,
                resolvedCanvasKey: currentResolvedKey);
        }

        private string AddCanvas(ResolvedCanvasKey resolvedCanvasKey, RenderColorSpace colorSpace)
        {
            if (!_canvasLookup.TryGetValue(resolvedCanvasKey, out var canvas))
                return $"missing-canvas:{resolvedCanvasKey.StableValue}";

            return AddCanvas(canvas, colorSpace);
        }

        private string AddCanvas(RenderCanvasSnapshot canvas, RenderColorSpace colorSpace)
        {
            var dependencies = new List<string>();
            foreach (var drawObject in canvas.Objects.Where(static item => item.Enabled))
            {
                switch (drawObject)
                {
                    case RenderSourceLayerDrawObjectSnapshot sourceLayer:
                        var sourceKey = AddNode(
                            MediaForgeRenderGraphNodeKind.SourceFrame,
                            $"source:{sourceLayer.SourceId}:frame:{ResolveSourceFrameNumber(sourceLayer.SourceId)}",
                            sourceLayer.Name,
                            sourceId: sourceLayer.SourceId);

                        var dependency = sourceKey;
                        var sourcePlan = EffectExecutionPlanner.Default.CreatePlan(
                            EffectScope.Source,
                            sourceLayer.SourceEffects);
                        if (!sourcePlan.IsEmpty)
                        {
                            var frame = sourceLayer.BoundFrame;
                            dependency = AddNode(
                                MediaForgeRenderGraphNodeKind.SourceEffectChain,
                                CreateSourceEffectKey(
                                    sourceLayer.SourceId,
                                    frame?.FrameNumber ?? -1,
                                    frame?.PixelFormat ?? "UNKNOWN",
                                    frame?.TextureSize ?? canvas.Size,
                                    colorSpace,
                                    sourcePlan.Fingerprint),
                                sourceLayer.Name,
                                [sourceKey],
                                sourceId: sourceLayer.SourceId);
                        }

                        var enabledEffects = GetEnabledEffects(sourceLayer);
                        dependencies.Add(enabledEffects.Count > 0
                            ? AddNode(
                                MediaForgeRenderGraphNodeKind.LayerEffectChain,
                                $"{CreateLayerEffectKey(canvas, sourceLayer, enabledEffects)}:input:{dependency}",
                                sourceLayer.Name,
                                [dependency],
                                canvasId: canvas.Id,
                                resolvedCanvasKey: canvas.PhysicalKey,
                                sourceId: sourceLayer.SourceId,
                                drawObjectId: sourceLayer.Id)
                            : dependency);
                        break;

                    case RenderCanvasDrawObjectSnapshot nested when nested.NestedCanvas is not null:
                        dependencies.Add(AddCanvas(nested.NestedCanvas, colorSpace));
                        break;

                    case RenderCanvasDrawObjectSnapshot nested when nested.NestedResolvedCanvasKey is { } nestedKey:
                        dependencies.Add(AddCanvas(nestedKey, colorSpace));
                        break;

                    case RenderAdjustmentLayerDrawObjectSnapshot adjustment:
                        AddAdjustmentLayerCheckpoint(canvas, adjustment, colorSpace, dependencies);
                        break;

                    case RenderTextDrawObjectSnapshot:
                    case RenderSolidDrawObjectSnapshot:
                        dependencies.Add(AddNode(
                            MediaForgeRenderGraphNodeKind.PrimitiveLayer,
                            $"primitive:{drawObject.Id}:{HashPrimitive(drawObject)}",
                            drawObject.Name,
                            canvasId: canvas.Id,
                            resolvedCanvasKey: canvas.PhysicalKey,
                            drawObjectId: drawObject.Id));
                        break;
                }
            }

            var canvasNode = AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.PhysicalKey.StableValue}:size:{canvas.Size.Width}x{canvas.Size.Height}:color-space:{colorSpace}:content:{HashCanvas(canvas)}",
                canvas.Name,
                dependencies,
                canvasId: canvas.Id,
                resolvedCanvasKey: canvas.PhysicalKey);

            return AddCanvasEffectChain(canvas, colorSpace, canvasNode);
        }

        private string AddCanvasEffectChain(
            RenderCanvasSnapshot canvas,
            RenderColorSpace colorSpace,
            string canvasNode)
        {
            var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Canvas, canvas.Effects);
            return plan.IsEmpty
                ? canvasNode
                : AddNode(
                    MediaForgeRenderGraphNodeKind.CanvasEffectChain,
                    $"canvas-effect:{canvas.PhysicalKey.StableValue}:size:{canvas.Size.Width}x{canvas.Size.Height}:color-space:{colorSpace}:stack:{plan.Fingerprint.Value}",
                    canvas.Name,
                    [canvasNode],
                    canvasId: canvas.Id,
                    resolvedCanvasKey: canvas.PhysicalKey);
        }

        private ResolvedCanvasKey ResolveOutputCanvasKey(RenderOutputStateSnapshot output)
        {
            if (!output.ResolvedCanvasKey.IsEmpty)
                return output.ResolvedCanvasKey;

            var candidates = snapshot.Canvases.Where(canvas => canvas.Id == output.CanvasId).ToArray();
            return candidates.Length == 1
                ? candidates[0].PhysicalKey
                : throw new InvalidOperationException(
                    $"Output '{output.Name}' does not identify one resolved canvas revision.");
        }

        private ResolvedCanvasKey ResolvePreviousCanvasKey(
            RenderOutputStateSnapshot output,
            Core.Identifiers.CanvasId previousCanvasId)
        {
            if (output.PreviousResolvedCanvasKey is { IsEmpty: false } resolved)
                return resolved;

            var candidates = snapshot.Canvases
                .Where(canvas => canvas.Id == previousCanvasId)
                .Select(static canvas => canvas.PhysicalKey)
                .Distinct()
                .ToArray();
            return candidates.Length == 1
                ? candidates[0]
                : throw new InvalidOperationException(
                    $"Output '{output.Name}' transition does not identify one previous resolved canvas revision.");
        }

        private long ResolveSourceFrameNumber(Core.Identifiers.SourceId sourceId) =>
            snapshot.FrameLeases
                .Where(lease => lease.Frame.SourceId == sourceId)
                .Select(static lease => lease.Frame.FrameNumber)
                .DefaultIfEmpty(-1)
                .Max();

        private static string ResolveCanvasVersionKey(SceneVersionBinding binding)
        {
            binding.Validate();
            return binding.Kind switch
            {
                SceneVersionBindingKind.Published => "published",
                SceneVersionBindingKind.Draft => $"draft:{binding.DraftSessionId!.Value.Value}",
                SceneVersionBindingKind.ExplicitVersion => $"explicit:{binding.ExplicitVersionId!.Value.Value}",
                _ => throw new InvalidOperationException($"Unsupported scene binding kind '{binding.Kind}'.")
            };
        }

        private string AddNode(
            MediaForgeRenderGraphNodeKind kind,
            string key,
            string name,
            IReadOnlyList<string>? dependencies = null,
            Core.Identifiers.RenderOutputId? outputId = null,
            Core.Identifiers.RenderOutputTypeId? outputTypeId = null,
            Core.Identifiers.CanvasId? canvasId = null,
            ResolvedCanvasKey? resolvedCanvasKey = null,
            Core.Identifiers.CanvasId? previousCanvasId = null,
            ResolvedCanvasKey? previousResolvedCanvasKey = null,
            Core.Identifiers.SourceId? sourceId = null,
            Core.Identifiers.DrawObjectId? drawObjectId = null)
        {
            if (_nodes.TryGetValue(key, out _))
                return key;

            _nodes.Add(
                key,
                new MediaForgeRenderGraphNode
                {
                    Kind = kind,
                    Key = key,
                    Name = name,
                    Dependencies = dependencies ?? [],
                    OutputId = outputId,
                    OutputTypeId = outputTypeId,
                    CanvasId = canvasId,
                    ResolvedCanvasKey = resolvedCanvasKey,
                    PreviousCanvasId = previousCanvasId,
                    PreviousResolvedCanvasKey = previousResolvedCanvasKey,
                    SourceId = sourceId,
                    DrawObjectId = drawObjectId
                });
            return key;
        }

        private void AddAdjustmentLayerCheckpoint(
            RenderCanvasSnapshot canvas,
            RenderAdjustmentLayerDrawObjectSnapshot adjustment,
            RenderColorSpace colorSpace,
            List<string> dependencies)
        {
            var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, adjustment.Effects);
            if (plan.IsEmpty)
                return;

            var checkpoint = AddNode(
                MediaForgeRenderGraphNodeKind.AdjustmentLayerCheckpoint,
                $"adjustment:{canvas.PhysicalKey.StableValue}:draw:{adjustment.Id}:color-space:{colorSpace}:state:{DrawObjectVisualStateFingerprint.Create(adjustment)}:effects:{plan.Fingerprint.Value}",
                adjustment.Name,
                dependencies.ToArray(),
                canvasId: canvas.Id,
                resolvedCanvasKey: canvas.PhysicalKey,
                drawObjectId: adjustment.Id);
            dependencies.Clear();
            dependencies.Add(checkpoint);
        }
    }

    private static IReadOnlyList<EffectStateSnapshot> GetEnabledEffects(DrawObjectStateSnapshot drawObject) =>
        EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, drawObject.Effects).OrderedEffects;

    private static IReadOnlyList<EffectStateSnapshot> GetEnabledEffects(RenderDrawObjectSnapshot drawObject) =>
        EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, drawObject.Effects).OrderedEffects;

    private static string CreateSourceEffectKey(
        Core.Identifiers.SourceId sourceId,
        long frameNumber,
        string pixelFormat,
        FrameSize resolution,
        RenderColorSpace colorSpace,
        EffectStackFingerprint fingerprint) =>
        $"source-effect:{sourceId}:frame:{frameNumber}:stack:{fingerprint.Value}:format:{pixelFormat}:resolution:{resolution.Width}x{resolution.Height}:color-space:{colorSpace}";

    private static string CreateLayerEffectKey(
        CanvasStateSnapshot canvas,
        SourceLayerDrawObjectSnapshot sourceLayer,
        IReadOnlyList<EffectStateSnapshot> effects)
    {
        var effectHash = HashEffects(effects);
        return $"layer-effect:{sourceLayer.SourceId}:canvas:{canvas.Id}:draw:{sourceLayer.Id}:local-size:{ResolveLocalLayerSize(sourceLayer.Transform)}:placement:{HashSourceEffectPlacement(canvas, sourceLayer)}:effects:{effectHash}";
    }

    private static string CreateLayerEffectKey(
        RenderCanvasSnapshot canvas,
        RenderSourceLayerDrawObjectSnapshot sourceLayer,
        IReadOnlyList<EffectStateSnapshot> effects)
    {
        var effectHash = HashEffects(effects);
        return $"layer-effect:{sourceLayer.SourceId}:canvas:{canvas.PhysicalKey.StableValue}:draw:{sourceLayer.Id}:local-size:{ResolveLocalLayerSize(sourceLayer.Transform)}:placement:{HashSourceEffectPlacement(canvas, sourceLayer)}:effects:{effectHash}";
    }

    private static string ResolveLocalLayerSize(WTK.MediaForge.Core.Geometry.Transform2D transform) =>
        $"{Math.Max(1, (uint)Math.Ceiling(transform.Size.Width))}x{Math.Max(1, (uint)Math.Ceiling(transform.Size.Height))}";

    private static string HashEffects(IReadOnlyList<EffectStateSnapshot> effects)
    {
        return EffectStackFingerprint.Create(effects).Value;
    }

    private static string HashPrimitive(DrawObjectStateSnapshot drawObject)
        => DrawObjectVisualStateFingerprint.Create(drawObject);

    private static string HashPrimitive(RenderDrawObjectSnapshot drawObject)
        => DrawObjectVisualStateFingerprint.Create(drawObject);

    private static string HashCanvas(CanvasStateSnapshot canvas)
        => CanvasVisualStateFingerprint.Create(canvas);

    private static string HashCanvas(RenderCanvasSnapshot canvas)
        => CanvasVisualStateFingerprint.Create(canvas);

    private static string HashSourceEffectPlacement(
        CanvasStateSnapshot canvas,
        SourceLayerDrawObjectSnapshot sourceLayer)
    {
        return $"{canvas.Size.Width}x{canvas.Size.Height}:{DrawObjectVisualStateFingerprint.Create(sourceLayer)}";
    }

    private static string HashSourceEffectPlacement(
        RenderCanvasSnapshot canvas,
        RenderSourceLayerDrawObjectSnapshot sourceLayer)
    {
        return $"{canvas.Size.Width}x{canvas.Size.Height}:{DrawObjectVisualStateFingerprint.Create(sourceLayer)}";
    }

}
