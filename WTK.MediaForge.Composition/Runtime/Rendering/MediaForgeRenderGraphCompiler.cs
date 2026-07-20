using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Serialization;
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
        private readonly Dictionary<Core.Identifiers.CanvasId, RenderCanvasSnapshot> _canvasLookup =
            snapshot.Canvases.ToDictionary(static canvas => canvas.Id);

        public IReadOnlyList<MediaForgeRenderGraphNode> Nodes => _nodes.Values.ToList();

        public string AddOutput(RenderOutputStateSnapshot output)
        {
            var canvasKey = AddCanvas(output.CanvasId);
            var dependency = canvasKey;

            if (output.RouteTransitionKind != OutputRouteTransitionKind.Cut &&
                output.PreviousCanvasId is { } previousCanvasId)
            {
                var previousCanvasKey = AddCanvas(previousCanvasId);
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
                $"output:{output.Id}:canvas:{output.CanvasId}:binding:{ResolveCanvasVersionKey(output.SceneVersionBinding)}:size:{output.OutputSize.Width}x{output.OutputSize.Height}:layout:{output.CanvasLayoutMode}:letterbox:{output.LetterboxColor.R:R},{output.LetterboxColor.G:R},{output.LetterboxColor.B:R},{output.LetterboxColor.A:R}:color-space:{output.ColorSpace}",
                output.Name,
                [dependency],
                outputId: output.Id,
                canvasId: output.CanvasId);
        }

        private string AddCanvas(Core.Identifiers.CanvasId canvasId)
        {
            if (!_canvasLookup.TryGetValue(canvasId, out var canvas))
                return $"missing-canvas:{canvasId}";

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
                                sourceId: sourceLayer.SourceId,
                                drawObjectId: HasPlacementDependentEffects(enabledEffects) ? sourceLayer.Id : null)
                            : sourceKey);
                        break;

                    case RenderCanvasDrawObjectSnapshot nested when nested.NestedCanvas is not null:
                        dependencies.Add(AddCanvas(nested.NestedCanvas));
                        break;

                    case RenderCanvasDrawObjectSnapshot nested:
                        dependencies.Add(AddCanvas(nested.NestedCanvasId));
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
                $"canvas:{canvas.Id}:version:{ResolveCanvasVersionKey(canvas)}:size:{canvas.Size.Width}x{canvas.Size.Height}:content:{HashCanvas(canvas)}",
                canvas.Name,
                dependencies,
                canvasId: canvas.Id);
        }

        private static string ResolveCanvasVersionKey(RenderCanvasSnapshot canvas) =>
            canvas.VersionId is { } version
                ? $"render:{version.Value}"
                : $"render-snapshot:{canvas.Id}";

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

        return $"source-effect:{sourceLayer.SourceId}:canvas:{canvas.Id}:draw:{sourceLayer.Id}:size:{canvas.Size.Width}x{canvas.Size.Height}:placement:{HashSourceEffectPlacement(canvas, sourceLayer)}:effects:{effectHash}";
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

        var json = JsonSerializer.Serialize(fingerprints, CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashPrimitive(DrawObjectStateSnapshot drawObject)
    {
        var json = JsonSerializer.Serialize(CreatePrimitiveFingerprint(drawObject), CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashPrimitive(RenderDrawObjectSnapshot drawObject)
    {
        var json = JsonSerializer.Serialize(CreatePrimitiveFingerprint(drawObject), CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashCanvas(CanvasStateSnapshot canvas)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                Background = new { canvas.BackgroundColor.R, canvas.BackgroundColor.G, canvas.BackgroundColor.B, canvas.BackgroundColor.A },
                Objects = canvas.Objects.Select(CreateDrawObjectFingerprint).ToArray()
            },
            CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashCanvas(RenderCanvasSnapshot canvas)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                Background = new { canvas.BackgroundColor.R, canvas.BackgroundColor.G, canvas.BackgroundColor.B, canvas.BackgroundColor.A },
                Objects = canvas.Objects.Select(CreateDrawObjectFingerprint).ToArray()
            },
            CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashSourceEffectPlacement(
        CanvasStateSnapshot canvas,
        SourceLayerDrawObjectSnapshot sourceLayer)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                CanvasWidth = canvas.Size.Width,
                CanvasHeight = canvas.Size.Height,
                sourceLayer.SourceId,
                sourceLayer.LayoutMode,
                LetterboxR = sourceLayer.LetterboxColor.R,
                LetterboxG = sourceLayer.LetterboxColor.G,
                LetterboxB = sourceLayer.LetterboxColor.B,
                LetterboxA = sourceLayer.LetterboxColor.A,
                sourceLayer.ContentRotationOverride,
                Common = CreateDrawObjectFingerprint(sourceLayer)
            },
            CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashSourceEffectPlacement(
        RenderCanvasSnapshot canvas,
        RenderSourceLayerDrawObjectSnapshot sourceLayer)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                CanvasWidth = canvas.Size.Width,
                CanvasHeight = canvas.Size.Height,
                sourceLayer.SourceId,
                sourceLayer.LayoutMode,
                LetterboxR = sourceLayer.LetterboxColor.R,
                LetterboxG = sourceLayer.LetterboxColor.G,
                LetterboxB = sourceLayer.LetterboxColor.B,
                LetterboxA = sourceLayer.LetterboxColor.A,
                sourceLayer.ContentRotationOverride,
                Common = CreateDrawObjectFingerprint(sourceLayer)
            },
            CreateFingerprintJsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static JsonSerializerOptions CreateFingerprintJsonOptions()
    {
        var options = MediaForgeProjectJsonOptions.Create();
        options.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
        return options;
    }

    private static object CreatePrimitiveFingerprint(DrawObjectStateSnapshot drawObject) =>
        drawObject switch
        {
            TextDrawObjectSnapshot text => new
            {
                Type = "primitive.text",
                text.Text,
                text.FontFamily,
                text.FontSize,
                TextR = text.TextColor.R,
                TextG = text.TextColor.G,
                TextB = text.TextColor.B,
                TextA = text.TextColor.A,
                Common = CreateDrawObjectFingerprint(text)
            },
            SolidDrawObjectSnapshot solid => new
            {
                Type = "primitive.solid",
                FillR = solid.FillColor.R,
                FillG = solid.FillColor.G,
                FillB = solid.FillColor.B,
                FillA = solid.FillColor.A,
                Common = CreateDrawObjectFingerprint(solid)
            },
            _ => new
            {
                Type = drawObject.GetType().FullName,
                Common = CreateDrawObjectFingerprint(drawObject)
            }
        };

    private static object CreatePrimitiveFingerprint(RenderDrawObjectSnapshot drawObject) =>
        drawObject switch
        {
            RenderTextDrawObjectSnapshot text => new
            {
                Type = "primitive.text",
                text.Text,
                text.FontFamily,
                text.FontSize,
                TextR = text.TextColor.R,
                TextG = text.TextColor.G,
                TextB = text.TextColor.B,
                TextA = text.TextColor.A,
                Common = CreateDrawObjectFingerprint(text)
            },
            RenderSolidDrawObjectSnapshot solid => new
            {
                Type = "primitive.solid",
                FillR = solid.FillColor.R,
                FillG = solid.FillColor.G,
                FillB = solid.FillColor.B,
                FillA = solid.FillColor.A,
                Common = CreateDrawObjectFingerprint(solid)
            },
            _ => new
            {
                Type = drawObject.GetType().FullName,
                Common = CreateDrawObjectFingerprint(drawObject)
            }
        };

    private static object CreateDrawObjectFingerprint(DrawObjectStateSnapshot drawObject) => new
    {
        drawObject.Enabled,
        X = drawObject.Transform.Position.X,
        Y = drawObject.Transform.Position.Y,
        Width = drawObject.Transform.Size.Width,
        Height = drawObject.Transform.Size.Height,
        PivotX = drawObject.Transform.Pivot.X,
        PivotY = drawObject.Transform.Pivot.Y,
        drawObject.Transform.RotationDegrees,
        drawObject.Opacity,
        drawObject.BlendMode,
        CropLeft = drawObject.Crop?.Left,
        CropTop = drawObject.Crop?.Top,
        CropRight = drawObject.Crop?.Right,
        CropBottom = drawObject.Crop?.Bottom,
        Effects = GetEnabledEffects(drawObject)
            .Select(CreateEffectFingerprint)
            .ToArray()
    };

    private static object CreateDrawObjectFingerprint(RenderDrawObjectSnapshot drawObject) => new
    {
        drawObject.Enabled,
        X = drawObject.Transform.Position.X,
        Y = drawObject.Transform.Position.Y,
        Width = drawObject.Transform.Size.Width,
        Height = drawObject.Transform.Size.Height,
        PivotX = drawObject.Transform.Pivot.X,
        PivotY = drawObject.Transform.Pivot.Y,
        drawObject.Transform.RotationDegrees,
        drawObject.Opacity,
        drawObject.BlendMode,
        CropLeft = drawObject.EffectiveCrop.Left,
        CropTop = drawObject.EffectiveCrop.Top,
        CropRight = drawObject.EffectiveCrop.Right,
        CropBottom = drawObject.EffectiveCrop.Bottom,
        Effects = GetEnabledEffects(drawObject)
            .Select(CreateEffectFingerprint)
            .ToArray()
    };

    private static string CreateEffectFingerprint(EffectStateSnapshot effect) =>
        EffectStateFingerprint.CreateSemanticConfiguration(effect);
}
