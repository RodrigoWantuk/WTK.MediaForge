using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Engine;

public sealed class StudioProjectSession
{
    private readonly StudioProjectEngineMapper _mapper;
    private MediaForgeProject _canonicalProject;

    private StudioProjectSession(
        StudioProjectEngineMapper mapper,
        MediaForgeProject canonicalProject,
        StudioDocument document)
    {
        _mapper = mapper;
        _canonicalProject = Clone(canonicalProject);
        Document = document;
    }

    public StudioDocument Document { get; }

    public static StudioProjectSession Open(
        StudioProjectEngineMapper mapper,
        MediaForgeProject project,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(project);
        MediaForgeProjectValidator.Validate(project).ThrowIfInvalid();
        var canonical = Clone(project);
        return new StudioProjectSession(mapper, canonical, mapper.CreateDocument(canonical, displayName));
    }

    public static StudioProjectSession Create(
        StudioProjectEngineMapper mapper,
        StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(document);
        return new StudioProjectSession(mapper, mapper.CreateProject(document), document);
    }

    public MediaForgeProject CreateValidatedSaveSnapshot(StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(document, Document))
            throw new InvalidOperationException("The Studio document does not belong to this project session.");

        var candidate = Clone(_canonicalProject);
        candidate.SourceDefinitions = MergeSources(candidate, document);
        candidate.Canvases = MergeCanvases(candidate, document);
        candidate.Outputs = MergeOutputs(candidate, document);
        MediaForgeProjectValidator.Validate(candidate).ThrowIfInvalid();
        return Clone(candidate);
    }

    public void CommitSavedSnapshot(MediaForgeProject savedProject)
    {
        ArgumentNullException.ThrowIfNull(savedProject);
        MediaForgeProjectValidator.Validate(savedProject).ThrowIfInvalid();
        _canonicalProject = Clone(savedProject);
    }

    public MediaForgeProject GetCanonicalSnapshot() => Clone(_canonicalProject);

    private List<MediaForgeSourceDefinition> MergeSources(
        MediaForgeProject candidate,
        StudioDocument document)
    {
        var existing = candidate.SourceDefinitions.ToDictionary(static source => source.Id);
        var result = new List<MediaForgeSourceDefinition>();
        foreach (var studioSource in document.Sources)
        {
            var sourceId = StudioEngineIdMap.SourceId(studioSource.Id);
            if (existing.TryGetValue(sourceId, out var canonical) &&
                studioSource.ProjectionKind != StudioProjectionKind.KnownEditable)
            {
                result.Add(canonical);
                continue;
            }

            var projected = _mapper.CreateSourceDefinition(studioSource);
            if (projected is null)
                continue;

            if (canonical is null || canonical.TypeId != projected.TypeId)
            {
                result.Add(projected);
                continue;
            }

            canonical.Name = projected.Name;
            CopyEditableSourceEndpoint(canonical, projected);
            result.Add(canonical);
        }

        return result;
    }

    private List<MediaForgeCanvas> MergeCanvases(
        MediaForgeProject candidate,
        StudioDocument document)
    {
        var existing = candidate.Canvases.ToDictionary(static canvas => canvas.Id);
        var result = new List<MediaForgeCanvas>(document.Scenes.Count);
        foreach (var scene in document.Scenes)
        {
            var canvasId = StudioEngineIdMap.CanvasId(scene.Id);
            if (!existing.TryGetValue(canvasId, out var canvas))
            {
                result.Add(_mapper.CreateCanvas(scene, document.Sources));
                continue;
            }

            canvas.Name = scene.DisplayName;
            canvas.Size = new(
                ToPositiveUInt(scene.Canvas.Width, nameof(scene.Canvas.Width)),
                ToPositiveUInt(scene.Canvas.Height, nameof(scene.Canvas.Height)));
            var editedBackground = StudioSceneMutationFactory.ParseColor(scene.Canvas.BackgroundColor);
            if (!IsSameUiColor(canvas.BackgroundColor, editedBackground))
                canvas.BackgroundColor = editedBackground;
            canvas.Objects = MergeLayers(canvas, scene, document.Sources);
            result.Add(canvas);
        }

        return result;
    }

    private List<MediaForgeDrawObject> MergeLayers(
        MediaForgeCanvas canvas,
        StudioScene scene,
        IEnumerable<StudioSource> sources)
    {
        var existing = canvas.Objects.ToDictionary(static layer => layer.Id);
        var result = new List<MediaForgeDrawObject>(scene.Layers.Count);
        foreach (var studioLayer in scene.Layers.OrderBy(static layer => layer.Order))
        {
            var layerId = StudioEngineIdMap.DrawObjectId(studioLayer.Id);
            if (!existing.TryGetValue(layerId, out var layer) || !LayerTypeMatches(layer, studioLayer))
            {
                result.Add(_mapper.CreateLayer(studioLayer, sources));
                continue;
            }

            layer.Name = studioLayer.Name;
            layer.Enabled = studioLayer.IsVisible;
            layer.Transform = StudioSceneMutationFactory.ToTransform(studioLayer);
            layer.Opacity = StudioSceneMutationFactory.ToOpacity(studioLayer.Transform.Opacity);
            layer.BlendMode = studioLayer.BlendMode switch
            {
                StudioBlendMode.Alpha => BlendMode.Normal,
                StudioBlendMode.Additive => BlendMode.Add,
                _ => throw new NotSupportedException($"Blend mode '{studioLayer.BlendMode}' is not supported.")
            };
            layer.Crop = ToNormalizedCrop(studioLayer);
            layer.Effects = MergeEffects(layer.Effects, studioLayer.Effects);
            switch (layer)
            {
                case SourceLayerDrawObject sourceLayer:
                    sourceLayer.SourceId = StudioEngineIdMap.SourceId(studioLayer.SourceId);
                    break;
                case CanvasDrawObject nestedLayer:
                    nestedLayer.NestedCanvasId = StudioEngineIdMap.CanvasId(studioLayer.SourceId);
                    break;
                case TextDrawObject textLayer:
                    textLayer.Text = studioLayer.SourceName;
                    break;
            }
            result.Add(layer);
        }

        return result;
    }

    private static LayerEffectStack MergeEffects(
        IEnumerable<MediaForgeEffect> canonicalEffects,
        IEnumerable<StudioEffect> studioEffects)
    {
        var existing = canonicalEffects.ToDictionary(static effect => effect.Id);
        var result = new LayerEffectStack();
        var order = 0;
        foreach (var studioEffect in studioEffects)
        {
            var effectId = StudioEngineIdMap.EffectId(studioEffect.Id);
            if (!existing.TryGetValue(effectId, out var effect))
            {
                result.Add(StudioSceneMutationFactory.ToEffect(studioEffect, order++));
                continue;
            }

            effect.Name = studioEffect.Name;
            effect.Enabled = studioEffect.IsEnabled;
            effect.Order = order++;
            switch (effect)
            {
                case ChromaKeyEffect chroma:
                    var editedKeyColor = StudioSceneMutationFactory.ParseColor(studioEffect.KeyColor);
                    if (!IsSameUiColor(chroma.KeyColor, editedKeyColor))
                        chroma.KeyColor = editedKeyColor;
                    chroma.Similarity = ToUnitSingle(studioEffect.Tolerance, nameof(studioEffect.Tolerance));
                    chroma.Smoothness = ToUnitSingle(studioEffect.EdgeSmooth, nameof(studioEffect.EdgeSmooth));
                    chroma.SpillReduction = ToUnitSingle(studioEffect.Spill, nameof(studioEffect.Spill));
                    break;
                case BlurEffect blur:
                    blur.Radius = ToNonNegativeSingle(studioEffect.BlurRadius, nameof(studioEffect.BlurRadius));
                    break;
                case ColorCorrectionEffect color:
                    color.Brightness = ToFiniteSingle(studioEffect.Brightness, nameof(studioEffect.Brightness));
                    color.Contrast = ToPositiveSingle(studioEffect.Contrast, nameof(studioEffect.Contrast));
                    color.Saturation = ToPositiveSingle(studioEffect.Saturation, nameof(studioEffect.Saturation));
                    color.HueDegrees = ToFiniteSingle(studioEffect.HueDegrees, nameof(studioEffect.HueDegrees));
                    break;
            }
            result.Add(effect);
        }

        return result;
    }

    private List<MediaForgeRenderOutput> MergeOutputs(
        MediaForgeProject candidate,
        StudioDocument document)
    {
        var existing = candidate.Outputs.ToDictionary(static output => output.Id);
        var result = new List<MediaForgeRenderOutput>(document.Outputs.Count);
        foreach (var studioOutput in document.Outputs)
        {
            var outputId = StudioEngineIdMap.RenderOutputId(studioOutput.Id);
            if (existing.TryGetValue(outputId, out var output) &&
                studioOutput.ProjectionKind != StudioProjectionKind.KnownEditable)
            {
                result.Add(output);
                continue;
            }

            var projected = _mapper.CreateOutput(studioOutput, document);
            if (output is null || output.TypeId != projected.TypeId)
            {
                result.Add(projected);
                continue;
            }

            output.Name = projected.Name;
            output.Enabled = projected.Enabled;
            output.CanvasId = projected.CanvasId;
            output.RouteTransition = projected.RouteTransition;
            CopyEditableOutputDestination(output, projected);
            result.Add(output);
        }

        return result;
    }

    private static void CopyEditableSourceEndpoint(
        MediaForgeSourceDefinition target,
        MediaForgeSourceDefinition projected)
    {
        if (target.TypeId == MediaSourceTypes.Webcam)
            CopyJsonProperty(target.Settings, projected.Settings, "deviceId");
        else if (target.TypeId == MediaSourceTypes.ImageFile || target.TypeId == MediaSourceTypes.VideoFile)
            CopyJsonProperty(target.Settings, projected.Settings, "path");
        else if (target.TypeId == MediaSourceTypes.NdiInput)
            CopyJsonProperty(target.Settings, projected.Settings, "sourceName");
        else if (target.TypeId == MediaSourceTypes.RtspInput)
            CopyJsonProperty(target.Settings, projected.Settings, "url");
    }

    private static void CopyEditableOutputDestination(
        MediaForgeRenderOutput target,
        MediaForgeRenderOutput projected)
    {
        if (target.TypeId == RenderOutputTypes.RecordingMp4)
            CopyJsonProperty(target.Settings, projected.Settings, "path");
        else if (target.TypeId == RenderOutputTypes.StreamingRtmp)
        {
            CopyJsonProperty(target.Settings, projected.Settings, "url");
            CopyJsonProperty(target.Settings, projected.Settings, "streamKey");
        }
        else if (target.TypeId == RenderOutputTypes.PreviewWindow)
            CopyJsonProperty(target.Settings, projected.Settings, "title");
    }

    private static void CopyJsonProperty(JsonObject target, JsonObject source, string propertyName)
    {
        if (source.TryGetPropertyValue(propertyName, out var value))
            target[propertyName] = value?.DeepClone();
    }

    private static bool LayerTypeMatches(MediaForgeDrawObject layer, StudioLayer studioLayer) =>
        studioLayer.Type switch
        {
            "Text" => layer is TextDrawObject,
            "Solid" => layer is SolidDrawObject,
            "Canvas" => layer is CanvasDrawObject,
            _ => layer is SourceLayerDrawObject
        };

    private static NormalizedRect? ToNormalizedCrop(StudioLayer layer)
    {
        if (layer.Crop == default)
            return null;

        var transform = StudioSceneMutationFactory.ToTransform(layer);
        var crop = new NormalizedRect(
            (float)Math.Clamp(layer.Crop.Left / transform.Size.Width, 0, 1),
            (float)Math.Clamp(layer.Crop.Top / transform.Size.Height, 0, 1),
            (float)Math.Clamp(1 - layer.Crop.Right / transform.Size.Width, 0, 1),
            (float)Math.Clamp(1 - layer.Crop.Bottom / transform.Size.Height, 0, 1));
        if (!crop.IsValid)
            throw new InvalidOperationException($"Layer '{layer.Name}' crop values remove the whole layer.");
        return crop;
    }

    private static uint ToPositiveUInt(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and positive.");
        return checked((uint)Math.Round(value));
    }

    private static float ToUnitSingle(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, $"{name} must be between zero and one.");
        return (float)value;
    }

    private static float ToNonNegativeSingle(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and non-negative.");
        return checked((float)value);
    }

    private static float ToFiniteSingle(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite.");
        return checked((float)value);
    }

    private static float ToPositiveSingle(double value, string name)
    {
        var result = ToFiniteSingle(value, name);
        if (result <= 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be positive.");
        return result;
    }

    private static bool IsSameUiColor(ColorRgba canonical, ColorRgba projected) =>
        ToColorByte(canonical.R) == ToColorByte(projected.R) &&
        ToColorByte(canonical.G) == ToColorByte(projected.G) &&
        ToColorByte(canonical.B) == ToColorByte(projected.B) &&
        ToColorByte(canonical.A) == ToColorByte(projected.A);

    private static int ToColorByte(float value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue);

    private static MediaForgeProject Clone(MediaForgeProject project) =>
        MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project));
}
