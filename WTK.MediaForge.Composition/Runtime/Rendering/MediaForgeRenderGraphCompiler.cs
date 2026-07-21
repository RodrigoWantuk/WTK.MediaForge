using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;

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
            var canvasKey = AddCanvas(output.CanvasId, output.SceneVersionBinding);
            var dependency = canvasKey;
            if (output.RouteTransitionKind != OutputRouteTransitionKind.Cut &&
                output.PreviousCanvasId is { } previousCanvasId)
            {
                var previousCanvasKey = AddCanvas(previousCanvasId, SceneVersionBinding.Published);
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
                canvasId: output.CanvasId);
        }

        private string AddCanvas(Core.Identifiers.CanvasId canvasId, SceneVersionBinding binding)
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

                        var enabledEffects = GetEnabledEffects(sourceLayer);
                        if (enabledEffects.Count > 0)
                        {
                            dependencies.Add(AddNode(
                                MediaForgeRenderGraphNodeKind.SourceEffectChain,
                                CreateSourceEffectKey(canvas, sourceLayer, enabledEffects),
                                sourceLayer.Name,
                                [sourceKey],
                                canvasId: HasPlacementDependentEffects(enabledEffects) ? canvas.Id : null,
                                sourceId: sourceLayer.SourceId,
                                drawObjectId: HasPlacementDependentEffects(enabledEffects) ? sourceLayer.Id : null));
                        }
                        else
                        {
                            dependencies.Add(sourceKey);
                        }

                        break;

                    case CanvasDrawObjectSnapshot nested:
                        dependencies.Add(AddCanvas(nested.NestedCanvasId, nested.VersionBinding));
                        break;

                    case TextDrawObjectSnapshot:
                    case SolidDrawObjectSnapshot:
                        dependencies.Add(AddNode(
                            MediaForgeRenderGraphNodeKind.PrimitiveLayer,
                            $"primitive:{drawObject.Id}:{HashPrimitive(drawObject)}",
                            drawObject.Name));
                        break;
                }
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.Id}:version:{versionKey}:size:{canvas.Size.Width}x{canvas.Size.Height}:content:{HashCanvas(canvas)}",
                canvas.Name,
                dependencies,
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
                    CanvasId = canvasId,
                    PreviousCanvasId = previousCanvasId,
                    SourceId = sourceId,
                    DrawObjectId = drawObjectId
                });
            return key;
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
            var canvasKey = AddCanvas(currentResolvedKey);
            var dependency = canvasKey;

            if (output.RouteTransitionKind != OutputRouteTransitionKind.Cut &&
                output.PreviousCanvasId is { } previousCanvasId)
            {
                var previousResolvedCanvasKey = ResolvePreviousCanvasKey(output, previousCanvasId);
                var previousCanvasKey = AddCanvas(previousResolvedCanvasKey);
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
                canvasId: output.CanvasId,
                resolvedCanvasKey: currentResolvedKey);
        }

        private string AddCanvas(ResolvedCanvasKey resolvedCanvasKey)
        {
            if (!_canvasLookup.TryGetValue(resolvedCanvasKey, out var canvas))
                return $"missing-canvas:{resolvedCanvasKey.StableValue}";

            return AddCanvas(canvas);
        }

        private string AddCanvas(RenderCanvasSnapshot canvas)
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

                        var enabledEffects = GetEnabledEffects(sourceLayer);
                        dependencies.Add(enabledEffects.Count > 0
                            ? AddNode(
                                MediaForgeRenderGraphNodeKind.SourceEffectChain,
                                $"{CreateSourceEffectKey(canvas, sourceLayer, enabledEffects)}:input:{sourceKey}",
                                sourceLayer.Name,
                                [sourceKey],
                                canvasId: HasPlacementDependentEffects(enabledEffects) ? canvas.Id : null,
                                resolvedCanvasKey: HasPlacementDependentEffects(enabledEffects) ? canvas.PhysicalKey : null,
                                sourceId: sourceLayer.SourceId,
                                drawObjectId: HasPlacementDependentEffects(enabledEffects) ? sourceLayer.Id : null)
                            : sourceKey);
                        break;

                    case RenderCanvasDrawObjectSnapshot nested when nested.NestedCanvas is not null:
                        dependencies.Add(AddCanvas(nested.NestedCanvas));
                        break;

                    case RenderCanvasDrawObjectSnapshot nested when nested.NestedResolvedCanvasKey is { } nestedKey:
                        dependencies.Add(AddCanvas(nestedKey));
                        break;

                    case RenderTextDrawObjectSnapshot:
                    case RenderSolidDrawObjectSnapshot:
                        dependencies.Add(AddNode(
                            MediaForgeRenderGraphNodeKind.PrimitiveLayer,
                            $"primitive:{drawObject.Id}:{HashPrimitive(drawObject)}",
                            drawObject.Name));
                        break;
                }
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.PhysicalKey.StableValue}:size:{canvas.Size.Width}x{canvas.Size.Height}:content:{HashCanvas(canvas)}",
                canvas.Name,
                dependencies,
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
                    CanvasId = canvasId,
                    ResolvedCanvasKey = resolvedCanvasKey,
                    PreviousCanvasId = previousCanvasId,
                    PreviousResolvedCanvasKey = previousResolvedCanvasKey,
                    SourceId = sourceId,
                    DrawObjectId = drawObjectId
                });
            return key;
        }
    }

    private static IReadOnlyList<EffectStateSnapshot> GetEnabledEffects(DrawObjectStateSnapshot drawObject) =>
        drawObject.Effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .ToArray();

    private static IReadOnlyList<EffectStateSnapshot> GetEnabledEffects(RenderDrawObjectSnapshot drawObject) =>
        drawObject.Effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .ToArray();

    private static string CreateSourceEffectKey(
        CanvasStateSnapshot canvas,
        SourceLayerDrawObjectSnapshot sourceLayer,
        IReadOnlyList<EffectStateSnapshot> effects)
    {
        var effectHash = HashEffects(effects);
        if (!HasPlacementDependentEffects(effects))
            return $"source-effect:{sourceLayer.SourceId}:{effectHash}";

        return $"source-effect:{sourceLayer.SourceId}:canvas:{canvas.Id}:draw:{sourceLayer.Id}:size:{canvas.Size.Width}x{canvas.Size.Height}:placement:{HashSourceEffectPlacement(canvas, sourceLayer)}:effects:{effectHash}";
    }

    private static string CreateSourceEffectKey(
        RenderCanvasSnapshot canvas,
        RenderSourceLayerDrawObjectSnapshot sourceLayer,
        IReadOnlyList<EffectStateSnapshot> effects)
    {
        var effectHash = HashEffects(effects);
        if (!HasPlacementDependentEffects(effects))
            return $"source-effect:{sourceLayer.SourceId}:{effectHash}";

        return $"source-effect:{sourceLayer.SourceId}:canvas:{canvas.PhysicalKey.StableValue}:draw:{sourceLayer.Id}:size:{canvas.Size.Width}x{canvas.Size.Height}:placement:{HashSourceEffectPlacement(canvas, sourceLayer)}:effects:{effectHash}";
    }

    private static bool HasPlacementDependentEffects(IReadOnlyList<EffectStateSnapshot> effects) =>
        effects.Any(static effect => effect is BlurEffectSnapshot);

    private static string HashEffects(IReadOnlyList<EffectStateSnapshot> effects)
    {
        var fingerprints = effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .Select(CreateEffectFingerprint)
            .ToArray();

        var canonical = string.Join('\u001F', fingerprints);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
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

    private static string CreateEffectFingerprint(EffectStateSnapshot effect) =>
        EffectStateFingerprint.CreateSemanticConfiguration(effect);
}
