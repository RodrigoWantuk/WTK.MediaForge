using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Effects;
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
    public StudioDocument CreateDocument(MediaForgeProject project, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        MediaForgeProjectValidator.Validate(project).ThrowIfInvalid();

        var document = new StudioDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Projeto MediaForge" : displayName,
            HasUnsavedChanges = false
        };
        var sourceNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var source in project.SourceDefinitions)
        {
            var id = source.Id.Value.ToString("D");
            var studioSource = new StudioSource
            {
                Id = id,
                DisplayName = source.Name,
                TypeId = ToStudioSourceType(source.TypeId),
                Endpoint = ReadSourceEndpoint(source),
                Metadata = source.TypeId.Value,
                Health = StudioHealthState.Healthy
            };
            document.Sources.Add(studioSource);
            sourceNames[id] = source.Name;
        }

        foreach (var canvas in project.Canvases)
        {
            var scene = new StudioScene
            {
                Id = canvas.Id.Value.ToString("D"),
                DisplayName = canvas.Name,
                Metadata = $"{canvas.Size.Width} × {canvas.Size.Height}",
                IsProgram = document.Scenes.Count == 0
            };
            scene.Canvas.Width = canvas.Size.Width;
            scene.Canvas.Height = canvas.Size.Height;
            scene.Canvas.BackgroundColor = ToHex(canvas.BackgroundColor);

            for (var index = 0; index < canvas.Objects.Count; index++)
                scene.Layers.Add(CreateStudioLayer(canvas.Objects[index], index, sourceNames));

            document.Scenes.Add(scene);
        }

        foreach (var output in project.Outputs)
        {
            var studioOutput = CreateStudioOutput(output);
            document.Outputs.Add(studioOutput);
            document.Scenes
                .FirstOrDefault(scene => scene.Id == studioOutput.AssignedSceneId)?
                .OutputIds.Add(studioOutput.Id);
        }

        document.SelectedSceneId = document.Scenes.FirstOrDefault()?.Id ?? string.Empty;
        return document;
    }

    public MediaForgeProject CreateProject(StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var project = new MediaForgeProject();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in document.Sources)
        {
            var definition = CreateSourceDefinition(source);
            if (definition is null)
                continue;

            project.SourceDefinitions.Add(definition);
            sourceIds.Add(source.Id);
        }

        foreach (var scene in document.Scenes)
            project.Canvases.Add(CreateCanvasCore(scene, sourceIds));

        foreach (var output in document.Outputs)
            project.Outputs.Add(CreateOutput(output, document));

        MediaForgeProjectValidator.Validate(project).ThrowIfInvalid();
        return project;
    }

    public MediaForgeSourceDefinition? CreateSourceDefinition(StudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var settings = CreateSourceSettings(source);
        return settings is null
            ? null
            : new MediaForgeSourceDefinition
            {
                Id = StudioEngineIdMap.SourceId(source.Id),
                Name = source.DisplayName,
                TypeId = settings.TypeId,
                SchemaVersion = settings.SchemaVersion,
                Settings = MediaSourceSettingsSerializer.ToJson(settings)
            };
    }

    public MediaForgeCanvas CreateCanvas(StudioScene scene, IEnumerable<StudioSource> sources)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sources);
        var sourceIds = sources
            .Where(source => CreateSourceSettings(source) is not null)
            .Select(static source => source.Id)
            .ToHashSet(StringComparer.Ordinal);
        return CreateCanvasCore(scene, sourceIds);
    }

    public MediaForgeDrawObject CreateLayer(StudioLayer layer, IEnumerable<StudioSource> sources)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(sources);

        var sourceIds = sources
            .Where(source => CreateSourceSettings(source) is not null)
            .Select(source => source.Id)
            .ToHashSet(StringComparer.Ordinal);

        return CreateDrawObject(layer, sourceIds);
    }

    private static MediaForgeCanvas CreateCanvasCore(
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
            "Canvas" => new CanvasDrawObject
            {
                NestedCanvasId = StudioEngineIdMap.CanvasId(layer.SourceId)
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

    public MediaForgeRenderOutput CreateOutput(
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
            Enabled = output.IsEnabled,
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

    private static StudioLayer CreateStudioLayer(
        MediaForgeDrawObject drawObject,
        int order,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        var sourceId = drawObject switch
        {
            SourceLayerDrawObject source => source.SourceId.Value.ToString("D"),
            CanvasDrawObject nested => nested.NestedCanvasId.Value.ToString("D"),
            _ => string.Empty
        };
        var layer = new StudioLayer
        {
            Id = drawObject.Id.Value.ToString("D"),
            Name = drawObject.Name,
            SourceId = sourceId,
            SourceName = drawObject is TextDrawObject text
                ? text.Text
                : sourceNames.GetValueOrDefault(sourceId, drawObject.Name),
            Type = drawObject switch
            {
                TextDrawObject => "Text",
                SolidDrawObject => "Solid",
                CanvasDrawObject => "Canvas",
                _ => "Source"
            },
            Order = order,
            IsVisible = drawObject.Enabled,
            BlendMode = drawObject.BlendMode == BlendMode.Add ? StudioBlendMode.Additive : StudioBlendMode.Alpha
        };
        layer.Transform.X = drawObject.Transform.Position.X;
        layer.Transform.Y = drawObject.Transform.Position.Y;
        layer.Transform.Width = drawObject.Transform.Size.Width;
        layer.Transform.Height = drawObject.Transform.Size.Height;
        layer.Transform.RotationDegrees = drawObject.Transform.RotationDegrees;
        layer.Transform.Opacity = drawObject.Opacity * 100;

        if (drawObject.Crop is { } crop)
        {
            layer.Crop = new StudioCropThickness(
                crop.Left * layer.Transform.Width,
                crop.Top * layer.Transform.Height,
                (1 - crop.Right) * layer.Transform.Width,
                (1 - crop.Bottom) * layer.Transform.Height);
        }

        foreach (var effect in drawObject.Effects.OrderBy(static effect => effect.Order))
            layer.Effects.Add(CreateStudioEffect(effect));

        return layer;
    }

    private static StudioEffect CreateStudioEffect(MediaForgeEffect effect)
    {
        var studio = new StudioEffect
        {
            Id = effect.Id.Value.ToString("D"),
            Name = string.IsNullOrWhiteSpace(effect.Name) ? effect.GetType().Name : effect.Name,
            Description = effect.GetType().Name,
            IsEnabled = effect.Enabled
        };

        if (effect is ChromaKeyEffect chroma)
        {
            studio.KeyColor = ToHex(chroma.KeyColor);
            studio.Tolerance = chroma.Similarity;
            studio.Spill = chroma.SpillReduction;
            studio.EdgeSmooth = chroma.Smoothness;
        }

        return studio;
    }

    private static StudioOutput CreateStudioOutput(MediaForgeRenderOutput output)
    {
        var studio = new StudioOutput
        {
            Id = output.Id.Value.ToString("D"),
            DisplayName = output.Name,
            TypeId = ToStudioOutputType(output.TypeId),
            AssignedSceneId = output.CanvasId.Value.ToString("D"),
            IsEnabled = output.Enabled,
            IsConfigured = true,
            State = StudioOutputState.Offline,
            DefaultTransitionId = output.RouteTransition.Id,
            TransitionDurationMs = output.RouteTransition.DurationMs
        };

        var settings = RenderOutputSettingsSerializer.Deserialize(output.TypeId, output.Settings);
        switch (settings)
        {
            case RecordingMp4OutputSettings recording:
                studio.Destination = recording.Path;
                studio.Codec = recording.Video.Codec.ToString();
                studio.Bitrate = $"{recording.Video.BitrateBitsPerSecond / 1_000_000d:0.##} Mbps";
                break;
            case StreamingRtmpOutputSettings streaming:
                studio.Destination = streaming.Url;
                studio.Secret = streaming.StreamKey;
                studio.Codec = streaming.Video.Codec.ToString();
                studio.Bitrate = $"{streaming.Video.BitrateBitsPerSecond / 1_000_000d:0.##} Mbps";
                break;
            case PreviewWindowOutputSettings preview:
                studio.Destination = preview.Title;
                break;
        }

        return studio;
    }

    private static string ToStudioSourceType(Core.Identifiers.MediaSourceTypeId typeId)
    {
        if (typeId == MediaSourceTypes.Desktop) return "source.desktop";
        if (typeId == MediaSourceTypes.Webcam) return "source.webcam";
        if (typeId == MediaSourceTypes.ImageFile) return "source.image";
        if (typeId == MediaSourceTypes.VideoFile) return "source.media";
        if (typeId == MediaSourceTypes.NdiInput) return "source.ndi";
        if (typeId == MediaSourceTypes.RtspInput) return "source.rtsp";
        throw new NotSupportedException($"Engine source type '{typeId}' cannot be opened by Studio.");
    }

    private static string ReadSourceEndpoint(MediaForgeSourceDefinition source)
    {
        var settings = MediaSourceSettingsSerializer.Deserialize(source.TypeId, source.Settings);
        return settings switch
        {
            WebcamSourceSettings webcam => webcam.DeviceId,
            ImageFileSourceSettings image => image.Path,
            VideoFileSourceSettings video => video.Path,
            NdiInputSourceSettings ndi => ndi.SourceName,
            RtspInputSourceSettings rtsp => rtsp.Url,
            DesktopCaptureSourceSettings desktop => $"adapter:{desktop.AdapterIndex}/output:{desktop.OutputIndex}",
            _ => string.Empty
        };
    }

    private static string ToStudioOutputType(Core.Identifiers.RenderOutputTypeId typeId)
    {
        if (typeId == RenderOutputTypes.PreviewWindow) return "output.preview";
        if (typeId == RenderOutputTypes.RecordingMp4) return "output.file.mp4";
        if (typeId == RenderOutputTypes.StreamingRtmp) return "output.rtmp";
        if (typeId == RenderOutputTypes.Ndi) return "output.ndi";
        if (typeId == RenderOutputTypes.VirtualCamera) return "output.virtual-camera";
        throw new NotSupportedException($"Engine output type '{typeId}' cannot be opened by Studio.");
    }

    private static string ToHex(ColorRgba color) =>
        $"#{ToByte(color.R):X2}{ToByte(color.G):X2}{ToByte(color.B):X2}{ToByte(color.A):X2}";

    private static int ToByte(float value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
