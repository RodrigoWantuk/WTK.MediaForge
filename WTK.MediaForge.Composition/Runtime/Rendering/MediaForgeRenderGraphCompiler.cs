using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Serialization;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal static class MediaForgeRenderGraphCompiler
{
    public static MediaForgeRenderGraphPlan Compile(MediaForgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var builder = new Builder(project);
        foreach (var output in project.Outputs)
            builder.AddOutput(output);

        return new MediaForgeRenderGraphPlan(builder.Nodes);
    }

    private sealed class Builder(MediaForgeProject project)
    {
        private readonly Dictionary<string, MediaForgeRenderGraphNode> _nodes = new(StringComparer.Ordinal);

        public IReadOnlyList<MediaForgeRenderGraphNode> Nodes => _nodes.Values.ToList();

        public string AddOutput(MediaForgeRenderOutput output)
        {
            var canvasKey = AddCanvas(output.CanvasId);
            return AddNode(
                MediaForgeRenderGraphNodeKind.OutputPass,
                $"output:{output.Id}:canvas:{output.CanvasId}:size:{output.OutputSize.Width}x{output.OutputSize.Height}:layout:{output.CanvasLayoutMode}",
                output.Name,
                [canvasKey]);
        }

        private string AddCanvas(Core.Identifiers.CanvasId canvasId)
        {
            var canvas = project.Canvases.FirstOrDefault(candidate => candidate.Id == canvasId);
            if (canvas is null)
                return $"missing-canvas:{canvasId}";

            var dependencies = new List<string>();
            foreach (var drawObject in canvas.Objects.Where(static item => item.Enabled))
            {
                switch (drawObject)
                {
                    case SourceLayerDrawObject sourceLayer:
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

                    case CanvasDrawObject nested:
                        dependencies.Add(AddCanvas(nested.NestedCanvasId));
                        break;
                }
            }

            return AddNode(
                MediaForgeRenderGraphNodeKind.CanvasRender,
                $"canvas:{canvas.Id}:size:{canvas.Size.Width}x{canvas.Size.Height}",
                canvas.Name,
                dependencies);
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

    private static IReadOnlyList<MediaForgeEffect> GetEnabledEffects(MediaForgeDrawObject drawObject) =>
        drawObject.Effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .ToArray();

    private static string HashEffects(IReadOnlyList<MediaForgeEffect> effects)
    {
        var fingerprints = effects
            .Where(static effect => effect.Enabled)
            .OrderBy(static effect => effect.Order)
            .Select(CreateEffectFingerprint)
            .ToArray();

        var json = JsonSerializer.Serialize(fingerprints, MediaForgeProjectJsonOptions.Create());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static object CreateEffectFingerprint(MediaForgeEffect effect) =>
        effect switch
        {
            ChromaKeyEffect chroma => new
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
            ColorCorrectionEffect color => new
            {
                Type = "effect.color",
                color.Order,
                color.Brightness,
                color.Contrast,
                color.Saturation,
                color.HueDegrees
            },
            BlurEffect blur => new
            {
                Type = "effect.blur",
                blur.Order,
                blur.Radius
            },
            TransitionEffect transition => new
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
