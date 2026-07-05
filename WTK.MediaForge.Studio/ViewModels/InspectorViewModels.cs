using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.Localization;
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
    private readonly LayerItemViewModel _layer;

    public LayerInspectorViewModel(string layerName, string sourceName)
        : this(new LayerItemViewModel(layerName, sourceName, "Source", StudioIconKind.Layer, 1)
        {
            X = 96,
            Y = 684,
            Width = 1280,
            Height = 148,
            Opacity = 92
        })
    {
    }

    public LayerInspectorViewModel(LayerItemViewModel layer)
        : base(StudioSelectionKind.Layer, layer.Name, $"{layer.Type} / {layer.Source}", layer.IconKind.ToString())
    {
        _layer = layer;
        _layer.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(LayerItemViewModel.Crop))
            {
                OnPropertyChanged(nameof(CropText));
            }
        };
        Effects = layer.Effects;
    }

    public double X
    {
        get => _layer.X;
        set
        {
            if (Math.Abs(_layer.X - value) > double.Epsilon)
            {
                _layer.X = value;
                OnPropertyChanged();
            }
        }
    }

    public double Y
    {
        get => _layer.Y;
        set
        {
            if (Math.Abs(_layer.Y - value) > double.Epsilon)
            {
                _layer.Y = value;
                OnPropertyChanged();
            }
        }
    }

    public double Width
    {
        get => _layer.Width;
        set
        {
            if (Math.Abs(_layer.Width - value) > double.Epsilon)
            {
                _layer.Width = value;
                OnPropertyChanged();
            }
        }
    }

    public double Height
    {
        get => _layer.Height;
        set
        {
            if (Math.Abs(_layer.Height - value) > double.Epsilon)
            {
                _layer.Height = value;
                OnPropertyChanged();
            }
        }
    }

    public double RotationDegrees
    {
        get => _layer.RotationDegrees;
        set
        {
            if (Math.Abs(_layer.RotationDegrees - value) > double.Epsilon)
            {
                _layer.RotationDegrees = value;
                OnPropertyChanged();
            }
        }
    }

    public StudioCropThickness Crop
    {
        get => _layer.Crop;
        set
        {
            _layer.Crop = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CropText));
        }
    }

    public string CropText => Crop.ToString();

    public double CropLeft
    {
        get => _layer.CropLeft;
        set
        {
            _layer.CropLeft = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CropText));
        }
    }

    public double CropTop
    {
        get => _layer.CropTop;
        set
        {
            _layer.CropTop = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CropText));
        }
    }

    public double CropRight
    {
        get => _layer.CropRight;
        set
        {
            _layer.CropRight = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CropText));
        }
    }

    public double CropBottom
    {
        get => _layer.CropBottom;
        set
        {
            _layer.CropBottom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CropText));
        }
    }

    public double Opacity
    {
        get => _layer.Opacity;
        set
        {
            _layer.Opacity = value;
            OnPropertyChanged();
        }
    }

    public StudioBlendMode BlendMode
    {
        get => _layer.BlendMode;
        set
        {
            _layer.BlendMode = value;
            OnPropertyChanged();
        }
    }

    public IEnumerable<StudioBlendMode> AvailableBlendModes => _layer.AvailableBlendModes;

    public bool IsVisible
    {
        get => _layer.IsVisible;
        set
        {
            _layer.IsVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsLocked
    {
        get => _layer.IsLocked;
        set
        {
            _layer.IsLocked = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<EffectItemViewModel> Effects { get; }
}

public sealed class SourceInspectorViewModel : InspectorPageViewModel
{
    public SourceInspectorViewModel(string sourceName, string sourceType, string endpoint)
        : base(StudioSelectionKind.Source, sourceName, new StudioDisplayNameService().GetSourceTypeName(sourceType), "SRC")
    {
        SourceType = new StudioDisplayNameService().GetSourceTypeName(sourceType);
        Endpoint = endpoint;
        Width = sourceType.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 2560 : 1920;
        Height = sourceType.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 1440 : 1080;
        FrameRate = sourceType.Contains("media", StringComparison.OrdinalIgnoreCase) ? 29.97 : 60;
        Status = "Healthy";
        DeviceOptions = sourceType.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? new ObservableCollection<string> { "Display 1", "Display 2", "Window Capture" }
            : new ObservableCollection<string> { "Logitech BRIO", "USB Camera", "Virtual Camera" };
        ResolutionOptions = new ObservableCollection<string> { "1920 x 1080", "1280 x 720", "2560 x 1440" };
        FrameRateOptions = new ObservableCollection<string> { "30 fps", "60 fps", "120 fps" };
        SelectedDevice = DeviceOptions[0];
        SelectedResolution = ResolutionOptions[0];
        SelectedFrameRate = FrameRateOptions[1];
    }

    public string SourceType { get; }

    public string Endpoint { get; }

    public int Width { get; }

    public int Height { get; }

    public string ResolutionText => $"{Width} x {Height}";

    public double FrameRate { get; }

    public string FrameRateText => $"{FrameRate:0.##} fps";

    public string Status { get; }

    public ObservableCollection<string> DeviceOptions { get; }

    public ObservableCollection<string> ResolutionOptions { get; }

    public ObservableCollection<string> FrameRateOptions { get; }

    public string SelectedDevice { get; set; }

    public string SelectedResolution { get; set; }

    public string SelectedFrameRate { get; set; }

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
    private string _destination;
    private string _codec;
    private string _bitrate;
    private string _health;
    private string _selectedOutputType;
    private string _selectedCodec;
    private string _selectedPreset;
    private int _keyframeSeconds = 2;

    public OutputInspectorViewModel(string outputName, string destination, string codec, string bitrate, string streamKey)
        : base(StudioSelectionKind.Output, outputName, "Render output route", "OUT")
    {
        _destination = destination;
        _codec = codec;
        _bitrate = bitrate;
        _health = "Ready";
        _streamKey = streamKey;
        OutputTypes = new ObservableCollection<string> { "Preview", "Recording MP4", "RTMP Streaming", "Virtual Camera" };
        CodecOptions = new ObservableCollection<string> { "H.264 (NVENC)", "H.264", "HEVC", "RGBA" };
        PresetOptions = new ObservableCollection<string> { "Quality", "Balanced", "Low Latency" };
        _selectedOutputType = new StudioDisplayNameService().GetOutputTypeName(string.IsNullOrWhiteSpace(streamKey) ? "output.file.mp4" : "output.rtmp");
        _selectedCodec = string.IsNullOrWhiteSpace(codec) ? CodecOptions[0] : codec;
        _selectedPreset = PresetOptions[0];
    }

    public string Destination
    {
        get => _destination;
        set => SetProperty(ref _destination, value);
    }

    public string Codec
    {
        get => _codec;
        set => SetProperty(ref _codec, value);
    }

    public string Bitrate
    {
        get => _bitrate;
        set => SetProperty(ref _bitrate, value);
    }

    public string Health
    {
        get => _health;
        set => SetProperty(ref _health, value);
    }

    public string MaskedStreamKey => string.IsNullOrWhiteSpace(_streamKey) ? "Not configured" : "sk_live_************";

    public ObservableCollection<string> OutputTypes { get; }

    public ObservableCollection<string> CodecOptions { get; }

    public ObservableCollection<string> PresetOptions { get; }

    public string SelectedOutputType
    {
        get => _selectedOutputType;
        set => SetProperty(ref _selectedOutputType, value);
    }

    public string SelectedCodec
    {
        get => _selectedCodec;
        set => SetProperty(ref _selectedCodec, value);
    }

    public string SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public int KeyframeSeconds
    {
        get => _keyframeSeconds;
        set => SetProperty(ref _keyframeSeconds, Math.Clamp(value, 1, 10));
    }

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
