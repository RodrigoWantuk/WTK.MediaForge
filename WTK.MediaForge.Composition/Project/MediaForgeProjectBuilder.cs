using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Effects;
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

    public MediaForgeProjectBuilder Scene(
        string name,
        int width,
        int height,
        out MediaForgeCanvas scene) =>
        Canvas(name, width, height, out scene);

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

    public MediaForgeProjectBuilder WebcamSource(
        string name,
        string deviceId,
        out MediaForgeSourceDefinition source,
        int? preferredWidth = null,
        int? preferredHeight = null,
        double? preferredFrameRate = null) =>
        Source(name, MediaForgeSources.Webcam(deviceId, preferredWidth, preferredHeight, preferredFrameRate), out source);

    public MediaForgeProjectBuilder ImageSource(
        string name,
        string path,
        out MediaForgeSourceDefinition source) =>
        Source(name, MediaForgeSources.Image(path), out source);

    public MediaForgeProjectBuilder AnimatedImageSource(
        string name,
        string path,
        out MediaForgeSourceDefinition source,
        bool loop = true,
        double? preferredFrameRate = null) =>
        Source(name, MediaForgeSources.AnimatedImage(path, loop, preferredFrameRate), out source);

    public MediaForgeProjectBuilder LottieSource(
        string name,
        string path,
        out MediaForgeSourceDefinition source,
        bool loop = true,
        double? preferredFrameRate = null) =>
        Source(name, MediaForgeSources.Lottie(path, loop, preferredFrameRate), out source);

    public MediaForgeProjectBuilder MediaFileSource(
        string name,
        string path,
        out MediaForgeSourceDefinition source,
        bool loop = true) =>
        Source(name, MediaForgeSources.MediaFile(path, loop), out source);

    public MediaForgeProjectBuilder RtspSource(
        string name,
        string url,
        out MediaForgeSourceDefinition source,
        RtspTransportMode transport = RtspTransportMode.Tcp) =>
        Source(name, MediaForgeSources.Rtsp(url, transport), out source);

    public MediaForgeProjectBuilder IpCameraSource(
        string name,
        string url,
        out MediaForgeSourceDefinition source,
        RtspTransportMode transport = RtspTransportMode.Tcp) =>
        Source(name, MediaForgeSources.IpCamera(url, transport), out source);

    public MediaForgeProjectBuilder NdiSource(
        string name,
        string sourceName,
        out MediaForgeSourceDefinition source) =>
        Source(name, MediaForgeSources.Ndi(sourceName), out source);

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
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, canvas, new OffscreenOutputSettings(), width, height, out output, configure);

    public MediaForgeProjectBuilder PreviewOutput(
        string name,
        MediaForgeCanvas scene,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        string title = "Preview",
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.PreviewWindow(title), width, height, out output, configure);

    public MediaForgeProjectBuilder RecordMp4Output(
        string name,
        MediaForgeCanvas scene,
        string path,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.RecordMp4(path), width, height, out output, configure);

    public MediaForgeProjectBuilder EncodedFileOutput(
        string name,
        MediaForgeCanvas scene,
        string path,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.EncodedFile(path), width, height, out output, configure);

    public MediaForgeProjectBuilder RtmpOutput(
        string name,
        MediaForgeCanvas scene,
        string url,
        string streamKey,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.Rtmp(url, streamKey), width, height, out output, configure);

    public MediaForgeProjectBuilder NdiOutput(
        string name,
        MediaForgeCanvas scene,
        string sourceName,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.Ndi(sourceName), width, height, out output, configure);

    public MediaForgeProjectBuilder VirtualCameraOutput(
        string name,
        MediaForgeCanvas scene,
        string deviceName,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null) =>
        Output(name, scene, MediaForgeOutputs.VirtualCamera(deviceName), width, height, out output, configure);

    public MediaForgeProjectBuilder Route(
        MediaForgeCanvas scene,
        MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(output);

        if (!_editor.Project.Canvases.Any(canvas => canvas.Id == scene.Id))
            throw new InvalidOperationException($"Scene {scene.Id} was not found in the project.");

        var projectOutput = _editor.Project.Outputs.FirstOrDefault(existing => existing.Id == output.Id)
            ?? throw new InvalidOperationException($"Output {output.Id} was not found in the project.");

        projectOutput.CanvasId = scene.Id;
        output.CanvasId = scene.Id;
        return this;
    }

    public MediaForgeProjectBuilder Output(
        string name,
        MediaForgeCanvas canvas,
        IRenderOutputSettings settings,
        int width,
        int height,
        out MediaForgeRenderOutput output,
        Action<MediaForgeRenderOutput>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        output = _editor.CreateOutput(name, canvas.Id, settings, ToFrameSize(width, height));
        configure?.Invoke(output);
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

    public SourceLayerBuilder SetLetterboxColor(ColorRgba color)
    {
        if (!color.IsInRange())
            throw new ArgumentOutOfRangeException(nameof(color), "Color components must be finite and between 0 and 1.");

        _layer.LetterboxColor = color;
        return this;
    }

    public SourceLayerBuilder SetLetterboxTransparent() =>
        SetLetterboxColor(ColorRgba.Transparent);

    public SourceLayerBuilder SetLetterboxBlack() =>
        SetLetterboxColor(ColorRgba.Black);

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

    public SourceLayerBuilder AddChromaKey(
        ColorRgba keyColor,
        float similarity = 0.4f,
        float smoothness = 0.08f,
        float spillReduction = 0.5f)
    {
        EnsureUnitRange(similarity, nameof(similarity));
        EnsureUnitRange(smoothness, nameof(smoothness));
        EnsureUnitRange(spillReduction, nameof(spillReduction));

        if (!keyColor.IsInRange())
            throw new ArgumentOutOfRangeException(nameof(keyColor), "Color components must be finite and between 0 and 1.");

        _layer.Effects.Add(new ChromaKeyEffect
        {
            KeyColor = keyColor,
            Similarity = similarity,
            Smoothness = smoothness,
            SpillReduction = spillReduction
        });
        return this;
    }

    public SourceLayerBuilder AddColorCorrection(
        float brightness = 0f,
        float contrast = 1f,
        float saturation = 1f,
        float hueDegrees = 0f)
    {
        EnsureFinite(brightness, nameof(brightness));
        EnsurePositive(contrast, nameof(contrast));
        EnsurePositive(saturation, nameof(saturation));
        EnsureFinite(hueDegrees, nameof(hueDegrees));

        _layer.Effects.Add(new ColorCorrectionEffect
        {
            Brightness = brightness,
            Contrast = contrast,
            Saturation = saturation,
            HueDegrees = hueDegrees
        });
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

    private static void EnsureFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
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
