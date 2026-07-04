using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public abstract class InspectorPageViewModel : ViewModelBase
{
    protected InspectorPageViewModel(StudioSelectionKind kind, string title, string subtitle, string icon)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
    }

    public StudioSelectionKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Icon { get; }
}

public sealed class EmptyInspectorViewModel : InspectorPageViewModel
{
    public EmptyInspectorViewModel()
        : base(StudioSelectionKind.None, "Nothing selected", "Select a scene, source, layer, or output.", "--")
    {
    }
}

public sealed class LayerInspectorViewModel : InspectorPageViewModel
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _rotationDegrees;
    private double _opacity;
    private StudioCropThickness _crop;
    private StudioBlendMode _blendMode;

    public LayerInspectorViewModel(string layerName, string sourceName)
        : base(StudioSelectionKind.Layer, layerName, sourceName, "LYR")
    {
        _x = 96;
        _y = 684;
        _width = 1280;
        _height = 148;
        _rotationDegrees = 0;
        _crop = new StudioCropThickness(0, 0, 0, 0);
        Opacity = 92;
        BlendMode = StudioBlendMode.Alpha;
        Effects = new ObservableCollection<EffectItemViewModel>
        {
            new("Chroma Key", "Green spill tightened, edge smooth 0.24", true, true),
            new("Blur", "Disabled for this layer", false, false)
        };
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set => SetProperty(ref _rotationDegrees, value);
    }

    public StudioCropThickness Crop
    {
        get => _crop;
        set
        {
            if (SetProperty(ref _crop, value))
            {
                OnPropertyChanged(nameof(CropText));
            }
        }
    }

    public string CropText => Crop.ToString();

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 0, 100));
    }

    public StudioBlendMode BlendMode
    {
        get => _blendMode;
        set => SetProperty(ref _blendMode, value);
    }

    public ObservableCollection<EffectItemViewModel> Effects { get; }
}

public sealed class SourceInspectorViewModel : InspectorPageViewModel
{
    public SourceInspectorViewModel(string sourceName, string sourceType, string endpoint)
        : base(StudioSelectionKind.Source, sourceName, sourceType, "SRC")
    {
        SourceType = sourceType;
        Endpoint = endpoint;
        Width = sourceType.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 2560 : 1920;
        Height = sourceType.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 1440 : 1080;
        FrameRate = sourceType.Contains("media", StringComparison.OrdinalIgnoreCase) ? 29.97 : 60;
        Status = "Healthy";
    }

    public string SourceType { get; }

    public string Endpoint { get; }

    public int Width { get; }

    public int Height { get; }

    public string ResolutionText => $"{Width} x {Height}";

    public double FrameRate { get; }

    public string FrameRateText => $"{FrameRate:0.##} fps";

    public string Status { get; }

    public ICommand? ReconnectCommand { get; init; }
}

public sealed class SceneInspectorViewModel : InspectorPageViewModel
{
    public SceneInspectorViewModel(string sceneName, string linkedOutputs)
        : base(StudioSelectionKind.Scene, sceneName, "Canvas scene", "SCN")
    {
        CanvasSize = "1920 x 1080";
        AspectRatio = "16:9";
        LinkedOutputs = linkedOutputs;
        LayerCount = 4;
        CompositionMode = "GPU render graph planned";
    }

    public string CanvasSize { get; }

    public string AspectRatio { get; }

    public string LinkedOutputs { get; }

    public int LayerCount { get; }

    public string CompositionMode { get; }
}

public sealed class OutputInspectorViewModel : InspectorPageViewModel
{
    private readonly string _streamKey;

    public OutputInspectorViewModel(string outputName, string destination, string codec, string bitrate, string streamKey)
        : base(StudioSelectionKind.Output, outputName, "Render output route", "OUT")
    {
        Destination = destination;
        Codec = codec;
        Bitrate = bitrate;
        Health = "Ready";
        _streamKey = streamKey;
    }

    public string Destination { get; }

    public string Codec { get; }

    public string Bitrate { get; }

    public string Health { get; }

    public string MaskedStreamKey => string.IsNullOrWhiteSpace(_streamKey) ? "Not configured" : "sk_live_************";

    internal string RawStreamKeyForTests => _streamKey;
}

public sealed class PresetInspectorViewModel : InspectorPageViewModel
{
    public PresetInspectorViewModel(string presetName, string description)
        : base(StudioSelectionKind.Preset, presetName, description, "PRE")
    {
        Canvas = "1920 x 1080";
        FrameRate = "60 fps";
        OutputProfile = "H.264 high profile";
    }

    public string Canvas { get; }

    public string FrameRate { get; }

    public string OutputProfile { get; }
}

public sealed class PackageInspectorViewModel : InspectorPageViewModel
{
    public PackageInspectorViewModel(string packageName, string description)
        : base(StudioSelectionKind.Package, packageName, description, "PKG")
    {
        Items = "Scenes, presets, source definitions";
        ImportMode = "Dry-run validation available";
    }

    public string Items { get; }

    public string ImportMode { get; }
}

public sealed class InspectorHostViewModel : ViewModelBase
{
    private InspectorPageViewModel _selectedPage = new EmptyInspectorViewModel();

    public InspectorPageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }
}
