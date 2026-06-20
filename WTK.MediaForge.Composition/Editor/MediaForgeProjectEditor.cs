using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Editor;

public sealed class MediaForgeProjectEditor
{
    public MediaForgeProjectEditor(MediaForgeProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public MediaForgeProject Project { get; }

    public MediaForgeCanvas CreateCanvas(string name, FrameSize size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (size.IsEmpty)
            throw new ArgumentException("Canvas size must be positive.", nameof(size));

        var canvas = new MediaForgeCanvas
        {
            Id = CanvasId.New(),
            Name = name,
            Size = size
        };

        Project.Canvases.Add(canvas);
        return canvas;
    }

    public MediaForgeSourceDefinition CreateSource(string name, IMediaSourceSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);

        var source = new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = name,
            TypeId = settings.TypeId,
            SchemaVersion = settings.SchemaVersion,
            Settings = MediaSourceSettingsSerializer.ToJson(settings)
        };

        Project.SourceDefinitions.Add(source);
        return source;
    }

    public SourceLayerDrawObject AddSourceLayer(CanvasId canvasId, SourceId sourceId, Transform2D transform)
    {
        var canvas = RequireCanvas(canvasId);
        EnsureSourceExists(sourceId);

        var layer = new SourceLayerDrawObject
        {
            Id = DrawObjectId.New(),
            Name = "Source Layer",
            SourceId = sourceId,
            Transform = transform
        };

        canvas.Objects.Add(layer);
        return layer;
    }

    public TextDrawObject AddText(CanvasId canvasId, string text, Transform2D transform)
    {
        var canvas = RequireCanvas(canvasId);

        var textObject = new TextDrawObject
        {
            Id = DrawObjectId.New(),
            Name = "Text",
            Text = text,
            Transform = transform
        };

        canvas.Objects.Add(textObject);
        return textObject;
    }

    public CanvasDrawObject AddCanvasLayer(CanvasId parentCanvasId, CanvasId nestedCanvasId, Transform2D transform)
    {
        if (parentCanvasId == nestedCanvasId)
            throw new InvalidOperationException("A canvas cannot reference itself.");

        var parentCanvas = RequireCanvas(parentCanvasId);
        _ = RequireCanvas(nestedCanvasId);

        var layer = new CanvasDrawObject
        {
            Id = DrawObjectId.New(),
            Name = "Nested Canvas",
            NestedCanvasId = nestedCanvasId,
            Transform = transform
        };

        parentCanvas.Objects.Add(layer);
        return layer;
    }

    public MediaForgeRenderOutput CreateOutput(
        string name,
        CanvasId canvasId,
        IRenderOutputSettings settings,
        FrameSize outputSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);

        _ = RequireCanvas(canvasId);

        if (outputSize.IsEmpty)
            throw new ArgumentException("Output size must be positive.", nameof(outputSize));

        var output = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = name,
            TypeId = settings.TypeId,
            SchemaVersion = settings.SchemaVersion,
            Settings = RenderOutputSettingsSerializer.ToJson(settings),
            CanvasId = canvasId,
            OutputSize = outputSize
        };

        Project.Outputs.Add(output);
        return output;
    }

    public void AddEffect(CanvasId canvasId, DrawObjectId objectId, MediaForgeEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var canvas = RequireCanvas(canvasId);
        var drawObject = canvas.Objects.FirstOrDefault(o => o.Id == objectId)
            ?? throw new InvalidOperationException($"Draw object {objectId} was not found in canvas '{canvas.Name}'.");

        if (effect.Order < 0)
            throw new ArgumentOutOfRangeException(nameof(effect), "Effect Order must be non-negative.");

        drawObject.Effects.Add(effect);
    }

    public ProjectValidationResult Validate() => MediaForgeProjectValidator.Validate(Project);

    public void ValidateOrThrow() => Validate().ThrowIfInvalid();

    private MediaForgeCanvas RequireCanvas(CanvasId canvasId)
    {
        var canvas = Project.Canvases.FirstOrDefault(c => c.Id == canvasId);
        return canvas ?? throw new InvalidOperationException($"Canvas {canvasId} was not found.");
    }

    private void EnsureSourceExists(SourceId sourceId)
    {
        if (!Project.SourceDefinitions.Any(s => s.Id == sourceId))
            throw new InvalidOperationException($"Source {sourceId} was not found.");
    }
}
