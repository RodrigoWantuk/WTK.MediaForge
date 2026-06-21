using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Project;

public sealed class MediaForgeProjectBuilder
{
    private readonly MediaForgeProjectEditor _editor;

    private MediaForgeProjectBuilder(MediaForgeProject project) =>
        _editor = new MediaForgeProjectEditor(project);

    public static MediaForgeProjectBuilder Create() => new(new MediaForgeProject());

    public static MediaForgeProjectBuilder FromProject(MediaForgeProject project) =>
        new(MediaForgeProjectCloner.DeepClone(project));

    public MediaForgeProjectBuilder Canvas(
        string name,
        int width,
        int height,
        out MediaForgeCanvas canvas)
    {
        canvas = _editor.CreateCanvas(name, ToFrameSize(width, height));
        return this;
    }

    public MediaForgeProjectBuilder DesktopSource(
        string name,
        int displayIndex,
        out MediaForgeSourceDefinition source) =>
        DesktopSource(name, adapterIndex: 0, outputIndex: displayIndex, out source);

    public MediaForgeProjectBuilder DesktopSource(
        string name,
        int adapterIndex,
        int outputIndex,
        out MediaForgeSourceDefinition source)
    {
        if (adapterIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(adapterIndex), "Adapter index must be non-negative.");

        if (outputIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(outputIndex), "Output index must be non-negative.");

        return Source(
            name,
            new DesktopCaptureSourceSettings
            {
                AdapterIndex = adapterIndex,
                OutputIndex = outputIndex
            },
            out source);
    }

    public MediaForgeProjectBuilder Source(
        string name,
        IMediaSourceSettings settings,
        out MediaForgeSourceDefinition source)
    {
        source = _editor.CreateSource(name, settings);
        return this;
    }

    public MediaForgeProjectBuilder AddSourceLayer(
        MediaForgeCanvas canvas,
        MediaForgeSourceDefinition source,
        Action<SourceLayerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(source);

        return AddSourceLayer(canvas.Id, source.Id, configure);
    }

    public MediaForgeProjectBuilder AddSourceLayer(
        CanvasId canvasId,
        SourceId sourceId,
        Action<SourceLayerBuilder>? configure = null)
    {
        var canvas = RequireCanvas(canvasId);
        var layer = _editor.AddSourceLayer(canvasId, sourceId, Transform2D.Default);

        try
        {
            configure?.Invoke(new SourceLayerBuilder(layer));
            return this;
        }
        catch
        {
            canvas.Objects.Remove(layer);
            throw;
        }
    }

    public MediaForgeProjectBuilder AddText(
        MediaForgeCanvas canvas,
        string text,
        Action<TextLayerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var layer = _editor.AddText(canvas.Id, text, Transform2D.Default);

        try
        {
            configure?.Invoke(new TextLayerBuilder(layer));
            return this;
        }
        catch
        {
            canvas.Objects.Remove(layer);
            throw;
        }
    }

    public MediaForgeProjectBuilder AddCanvasLayer(
        MediaForgeCanvas parentCanvas,
        MediaForgeCanvas nestedCanvas,
        Action<CanvasLayerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(parentCanvas);
        ArgumentNullException.ThrowIfNull(nestedCanvas);

        var layer = _editor.AddCanvasLayer(parentCanvas.Id, nestedCanvas.Id, Transform2D.Default);

        try
        {
            configure?.Invoke(new CanvasLayerBuilder(layer));
            return this;
        }
        catch
        {
            parentCanvas.Objects.Remove(layer);
            throw;
        }
    }

    public MediaForgeProjectBuilder OffscreenOutput(
        string name,
        MediaForgeCanvas canvas,
        int width,
        int height,
        out MediaForgeRenderOutput output) =>
        Output(name, canvas, new OffscreenOutputSettings(), width, height, out output);

    public MediaForgeProjectBuilder Output(
        string name,
        MediaForgeCanvas canvas,
        IRenderOutputSettings settings,
        int width,
        int height,
        out MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        output = _editor.CreateOutput(name, canvas.Id, settings, ToFrameSize(width, height));
        return this;
    }

    public MediaForgeProject Build() => MediaForgeProjectCloner.DeepClone(_editor.Project);

    public MediaForgeProject BuildValidated()
    {
        var project = Build();
        MediaForgeProjectValidator.Validate(project).ThrowIfInvalid();
        return project;
    }

    private MediaForgeCanvas RequireCanvas(CanvasId canvasId) =>
        _editor.Project.Canvases.FirstOrDefault(canvas => canvas.Id == canvasId)
        ?? throw new InvalidOperationException($"Canvas {canvasId} was not found.");

    private static FrameSize ToFrameSize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        return new FrameSize((uint)width, (uint)height);
    }
}

public sealed class SourceLayerBuilder
{
    private readonly SourceLayerDrawObject _layer;

    internal SourceLayerBuilder(SourceLayerDrawObject layer) =>
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));

    public SourceLayerBuilder SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _layer.Name = name;
        return this;
    }

    public SourceLayerBuilder SetBounds(float x, float y, float width, float height)
    {
        _layer.Transform = WithBounds(_layer.Transform, x, y, width, height);
        return this;
    }

    public SourceLayerBuilder SetFit()
    {
        _layer.LayoutMode = LayoutMode.Fit;
        return this;
    }

    public SourceLayerBuilder SetFill()
    {
        _layer.LayoutMode = LayoutMode.Fill;
        return this;
    }

    public SourceLayerBuilder SetStretch()
    {
        _layer.LayoutMode = LayoutMode.Stretch;
        return this;
    }

    public SourceLayerBuilder SetOpacity(float opacity)
    {
        EnsureUnitRange(opacity, nameof(opacity));
        _layer.Opacity = opacity;
        return this;
    }

    public SourceLayerBuilder SetBlendMode(BlendMode blendMode)
    {
        _layer.BlendMode = blendMode;
        return this;
    }

    private static Transform2D WithBounds(Transform2D current, float x, float y, float width, float height)
    {
        EnsurePositive(width, nameof(width));
        EnsurePositive(height, nameof(height));

        return new Transform2D
        {
            Position = new CanvasPoint(x, y),
            Size = new CanvasSize(width, height),
            RotationDegrees = current.RotationDegrees,
            Pivot = current.Pivot
        };
    }

    private static void EnsurePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
    }

    private static void EnsureUnitRange(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and between 0 and 1.");
    }
}

public sealed class TextLayerBuilder
{
    private readonly TextDrawObject _layer;

    internal TextLayerBuilder(TextDrawObject layer) =>
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));

    public TextLayerBuilder SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _layer.Name = name;
        return this;
    }

    public TextLayerBuilder SetBounds(float x, float y, float width, float height)
    {
        _layer.Transform = WithBounds(_layer.Transform, x, y, width, height);
        return this;
    }

    public TextLayerBuilder SetFontSize(float fontSize)
    {
        if (!float.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be finite and positive.");

        _layer.FontSize = fontSize;
        return this;
    }

    public TextLayerBuilder SetTextColor(ColorRgba color)
    {
        _layer.TextColor = color;
        return this;
    }

    public TextLayerBuilder SetOpacity(float opacity)
    {
        EnsureUnitRange(opacity, nameof(opacity));
        _layer.Opacity = opacity;
        return this;
    }

    private static Transform2D WithBounds(Transform2D current, float x, float y, float width, float height)
    {
        EnsurePositive(width, nameof(width));
        EnsurePositive(height, nameof(height));

        return new Transform2D
        {
            Position = new CanvasPoint(x, y),
            Size = new CanvasSize(width, height),
            RotationDegrees = current.RotationDegrees,
            Pivot = current.Pivot
        };
    }

    private static void EnsurePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
    }

    private static void EnsureUnitRange(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and between 0 and 1.");
    }
}

public sealed class CanvasLayerBuilder
{
    private readonly CanvasDrawObject _layer;

    internal CanvasLayerBuilder(CanvasDrawObject layer) =>
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));

    public CanvasLayerBuilder SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _layer.Name = name;
        return this;
    }

    public CanvasLayerBuilder SetBounds(float x, float y, float width, float height)
    {
        _layer.Transform = WithBounds(_layer.Transform, x, y, width, height);
        return this;
    }

    public CanvasLayerBuilder SetOpacity(float opacity)
    {
        EnsureUnitRange(opacity, nameof(opacity));
        _layer.Opacity = opacity;
        return this;
    }

    private static Transform2D WithBounds(Transform2D current, float x, float y, float width, float height)
    {
        EnsurePositive(width, nameof(width));
        EnsurePositive(height, nameof(height));

        return new Transform2D
        {
            Position = new CanvasPoint(x, y),
            Size = new CanvasSize(width, height),
            RotationDegrees = current.RotationDegrees,
            Pivot = current.Pivot
        };
    }

    private static void EnsurePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
    }

    private static void EnsureUnitRange(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and between 0 and 1.");
    }
}
