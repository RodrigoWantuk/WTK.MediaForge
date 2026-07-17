using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
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

    private sealed class Builder(ProjectStateSnapshot projectState)
    {
        private readonly Dictionary<string, MediaForgeRenderGraphNode> _nodes = new(StringComparer.Ordinal);

        public IReadOnlyList<MediaForgeRenderGraphNode> Nodes => _nodes.Values.ToList();

        public string AddOutput(RenderOutputStateSnapshot output)
        {
            var canvasKey = AddCanvas(output.CanvasId, output.SceneVersionBinding);
            return AddNode(
                MediaForgeRenderGraphNodeKind.OutputPass,
                $"output:{output.Id}:canvas:{output.CanvasId}:binding:{ResolveCanvasVersionKey(output.CanvasId, output.SceneVersionBinding)}:size:{output.OutputSize.Width}x{output.OutputSize.Height}:layout:{output.CanvasLayoutMode}",
                output.Name,
                [canvasKey]);
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
                            sourceLayer.Name);

                        var enabledEffects = GetEnabledEffects(sourceLayer);
                        if (enabledEffects.Count > 0)
                        {
                            dependencies.Add(AddNode(
                                MediaForgeRenderGraphNodeKind.SourceEffectChain,
                                $"source-effect:{sourceLayer.SourceId}:{HashEffects(enabledEffects)}",
                                sourceLayer.Name,
                                [sourceKey]));
                        }
                        else
                        {
                            dependencies.Add(sourceKey);
                        }

                        break;

                    case CanvasDrawObjectSnapshot nested:
                        dependencies.Add(AddCanvas(nested.NestedCanvasId, nested.VersionBinding));
                        break;
                }
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.Id}:version:{versionKey}:size:{canvas.Size.Width}x{canvas.Size.Height}",
                canvas.Name,
                dependencies);
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
            IReadOnlyList<string>? dependencies = null)
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
                    Dependencies = dependencies ?? []
                });
            return key;
        }
    }

    private static IReadOnlyList<EffectStateSnapshot> GetEnabledEffects(DrawObjectStateSnapshot drawObject) =>
        drawObject.Effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .ToArray();

    private static string HashEffects(IReadOnlyList<EffectStateSnapshot> effects)
    {
        var fingerprints = effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .Select(CreateEffectFingerprint)
            .ToArray();

        var json = JsonSerializer.Serialize(fingerprints, MediaForgeProjectJsonOptions.Create());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static object CreateEffectFingerprint(EffectStateSnapshot effect) =>
        effect switch
        {
            ChromaKeyEffectSnapshot chroma => new
            {
                Type = "effect.chroma",
                chroma.Order,
                KeyR = chroma.KeyColor.R,
                KeyG = chroma.KeyColor.G,
                KeyB = chroma.KeyColor.B,
                KeyA = chroma.KeyColor.A,
                chroma.Similarity,
                chroma.Smoothness,
                chroma.SpillReduction
            },
            ColorCorrectionEffectSnapshot color => new
            {
                Type = "effect.color",
                color.Order,
                color.Brightness,
                color.Contrast,
                color.Saturation,
                color.HueDegrees
            },
            BlurEffectSnapshot blur => new
            {
                Type = "effect.blur",
                blur.Order,
                blur.Radius
            },
            TransitionEffectSnapshot transition => new
            {
                Type = "effect.transition",
                transition.Order,
                transition.Kind,
                transition.Progress,
                transition.DurationSeconds
            },
            _ => new
            {
                Type = effect.GetType().FullName,
                effect.Order,
                effect.SchemaVersion
            }
        };
}
