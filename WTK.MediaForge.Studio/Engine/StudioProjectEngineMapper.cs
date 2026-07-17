using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Engine;

public sealed class StudioProjectEngineMapper
{
    public MediaForgeProject CreateProject(StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var project = new MediaForgeProject();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in document.Sources)
        {
            var settings = CreateSourceSettings(source);
            if (settings is null)
                continue;

            project.SourceDefinitions.Add(new MediaForgeSourceDefinition
            {
                Id = StudioEngineIdMap.SourceId(source.Id),
                Name = source.DisplayName,
                TypeId = settings.TypeId,
                SchemaVersion = settings.SchemaVersion,
                Settings = MediaSourceSettingsSerializer.ToJson(settings)
            });
            sourceIds.Add(source.Id);
        }

        foreach (var scene in document.Scenes)
            project.Canvases.Add(CreateCanvas(scene, sourceIds));

        foreach (var output in document.Outputs.Where(static output => output.IsEnabled))
            project.Outputs.Add(CreateOutput(output, document));

        MediaForgeProjectValidator.Validate(project).ThrowIfInvalid();
        return project;
    }

    private static MediaForgeCanvas CreateCanvas(
        StudioScene scene,
        IReadOnlySet<string> sourceIds)
    {
        var canvas = new MediaForgeCanvas
        {
            Id = StudioEngineIdMap.CanvasId(scene.Id),
            Name = scene.DisplayName,
            Size = ToFrameSize(scene.Canvas.Width, scene.Canvas.Height),
            BackgroundColor = StudioSceneMutationFactory.ParseColor(scene.Canvas.BackgroundColor)
        };

        foreach (var layer in scene.Layers.OrderBy(static layer => layer.Order))
            canvas.Objects.Add(CreateDrawObject(layer, sourceIds));

        return canvas;
    }

    private static MediaForgeDrawObject CreateDrawObject(
        StudioLayer layer,
        IReadOnlySet<string> sourceIds)
    {
        MediaForgeDrawObject drawObject = layer.Type switch
        {
            "Text" => new TextDrawObject
            {
                Text = layer.SourceName,
                FontFamily = TextDrawObject.DefaultFontFamily,
                FontSize = Math.Max(8f, StudioSceneMutationFactory.ToTransform(layer).Size.Height * 0.35f)
            },
            "Solid" => new SolidDrawObject
            {
                FillColor = ColorRgba.Black
            },
            _ => CreateSourceLayer(layer, sourceIds)
        };

        ApplyCommonLayerState(drawObject, layer);
        return drawObject;
    }

    private static SourceLayerDrawObject CreateSourceLayer(
        StudioLayer layer,
        IReadOnlySet<string> sourceIds)
    {
        if (!sourceIds.Contains(layer.SourceId))
        {
            throw new InvalidOperationException(
                $"Layer '{layer.Name}' references source '{layer.SourceId}', but that source is not exportable to the engine project.");
        }

        return new SourceLayerDrawObject
        {
            SourceId = StudioEngineIdMap.SourceId(layer.SourceId),
            LayoutMode = LayoutMode.Fill
        };
    }

    private static void ApplyCommonLayerState(MediaForgeDrawObject drawObject, StudioLayer layer)
    {
        drawObject.Id = StudioEngineIdMap.DrawObjectId(layer.Id);
        drawObject.Name = layer.Name;
        drawObject.Enabled = layer.IsVisible;
        drawObject.Transform = StudioSceneMutationFactory.ToTransform(layer);
        drawObject.Opacity = StudioSceneMutationFactory.ToOpacity(layer.Transform.Opacity);
        drawObject.BlendMode = ToBlendMode(layer.BlendMode);
        drawObject.Crop = ToNormalizedCrop(layer);
        drawObject.Effects = layer.Effects
            .Select((effect, order) => StudioSceneMutationFactory.ToEffect(effect, order))
            .ToList();
    }

    private static IMediaSourceSettings? CreateSourceSettings(StudioSource source)
    {
        return source.TypeId switch
        {
            "source.desktop" => MediaForgeSources.Desktop(),
            "source.webcam" => MediaForgeSources.Webcam(source.Endpoint),
            "source.image" => MediaForgeSources.Image(source.Endpoint),
            "source.media" => MediaForgeSources.MediaFile(source.Endpoint),
            "source.ndi" => MediaForgeSources.Ndi(source.Endpoint),
            "source.rtsp" => MediaForgeSources.Rtsp(source.Endpoint),
            "source.text" or "source.solid" => null,
            _ => throw new NotSupportedException($"Studio source type '{source.TypeId}' cannot be mapped to an engine source.")
        };
    }

    private static MediaForgeRenderOutput CreateOutput(
        StudioOutput output,
        StudioDocument document)
    {
        var assignedScene = document.Scenes.FirstOrDefault(scene => scene.Id == output.AssignedSceneId)
            ?? throw new InvalidOperationException($"Output '{output.DisplayName}' references missing scene '{output.AssignedSceneId}'.");

        var settings = CreateOutputSettings(output);
        return new MediaForgeRenderOutput
        {
            Id = StudioEngineIdMap.RenderOutputId(output.Id),
            Name = output.DisplayName,
            TypeId = settings.TypeId,
            SchemaVersion = settings.SchemaVersion,
            Settings = RenderOutputSettingsSerializer.ToJson(settings),
            CanvasId = StudioEngineIdMap.CanvasId(assignedScene.Id),
            OutputSize = ToFrameSize(assignedScene.Canvas.Width, assignedScene.Canvas.Height),
            CanvasLayoutMode = LayoutMode.Fit,
            LetterboxColor = ColorRgba.Black,
            RouteTransition = ToRouteTransition(output, document)
        };
    }

    private static IRenderOutputSettings CreateOutputSettings(StudioOutput output)
    {
        return output.TypeId switch
        {
            "output.preview" => MediaForgeOutputs.PreviewWindow(output.DisplayName),
            "output.file.mp4" => MediaForgeOutputs.RecordMp4(string.IsNullOrWhiteSpace(output.Destination) ? "recording.mp4" : output.Destination),
            "output.rtmp" => MediaForgeOutputs.Rtmp(output.Destination, output.Secret),
            "output.ndi" => MediaForgeOutputs.Ndi(output.Destination),
            "output.virtual-camera" => MediaForgeOutputs.VirtualCamera(output.Destination),
            _ => throw new NotSupportedException($"Studio output type '{output.TypeId}' cannot be mapped to an engine output.")
        };
    }

    private static OutputRouteTransition ToRouteTransition(
        StudioOutput output,
        StudioDocument document)
    {
        var transition = document.Transitions.FirstOrDefault(item => item.Id == output.DefaultTransitionId);
        if (transition is null || transition.Kind == StudioTransitionKind.Cut || output.TransitionDurationMs <= 0)
            return OutputRouteTransition.Cut(output.DefaultTransitionId);

        return OutputRouteTransition.Fade(
            output.DefaultTransitionId,
            output.TransitionDurationMs,
            transition.DisplayName);
    }

    private static FrameSize ToFrameSize(double width, double height) =>
        new(ToPositiveUInt(width, nameof(width)), ToPositiveUInt(height, nameof(height)));

    private static BlendMode ToBlendMode(StudioBlendMode blendMode) =>
        blendMode switch
        {
            StudioBlendMode.Alpha => BlendMode.Normal,
            StudioBlendMode.Additive => BlendMode.Add,
            _ => throw new NotSupportedException($"Blend mode '{blendMode}' is not supported by the engine mapper yet.")
        };

    private static NormalizedRect? ToNormalizedCrop(StudioLayer layer)
    {
        if (layer.Crop == default)
            return null;

        var width = StudioSceneMutationFactory.ToTransform(layer).Size.Width;
        var height = StudioSceneMutationFactory.ToTransform(layer).Size.Height;
        var crop = new NormalizedRect(
            (float)Math.Clamp(layer.Crop.Left / width, 0, 1),
            (float)Math.Clamp(layer.Crop.Top / height, 0, 1),
            (float)Math.Clamp(1 - layer.Crop.Right / width, 0, 1),
            (float)Math.Clamp(1 - layer.Crop.Bottom / height, 0, 1));

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
}
