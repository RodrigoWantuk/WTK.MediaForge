using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.DocumentModel;
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
        : base(StudioSelectionKind.None, "Nada selecionado", "Selecione uma cena, fonte, camada ou saida.", "--")
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
        : base(StudioSelectionKind.Layer, layer.Name, $"{new StudioDisplayNameService().GetLayerTypeName(layer.Type)} / {layer.Source}", layer.IconKind.ToString())
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
    public SourceInspectorViewModel(StudioSource source, string currentSceneName, ICommand? addToSceneCommand, ICommand? reconnectCommand)
        : base(StudioSelectionKind.Source, source.DisplayName, new StudioDisplayNameService().GetSourceTypeName(source.TypeId), "SRC")
    {
        SourceType = new StudioDisplayNameService().GetSourceTypeName(source.TypeId);
        Endpoint = source.Endpoint;
        CurrentSceneName = currentSceneName;
        Width = source.TypeId.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 2560 : 1920;
        Height = source.TypeId.Contains("desktop", StringComparison.OrdinalIgnoreCase) ? 1440 : 1080;
        FrameRate = source.TypeId.Contains("media", StringComparison.OrdinalIgnoreCase) ? 29.97 : 60;
        Status = new StudioDisplayNameService().GetHealthName(source.Health);
        DeviceOptions = source.TypeId.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? new ObservableCollection<string> { "Monitor 1", "Monitor 2", "Janela" }
            : new ObservableCollection<string> { "Logitech BRIO", "USB Camera", "Camera virtual" };
        ResolutionOptions = new ObservableCollection<string> { "1920 x 1080", "1280 x 720", "2560 x 1440" };
        FrameRateOptions = new ObservableCollection<string> { "30 fps", "60 fps", "120 fps" };
        SelectedDevice = DeviceOptions[0];
        SelectedResolution = ResolutionOptions[0];
        SelectedFrameRate = FrameRateOptions[1];
        AddToSceneCommand = addToSceneCommand;
        ReconnectCommand = reconnectCommand;
    }

    public string SourceType { get; }

    public string Endpoint { get; }

    public string CurrentSceneName { get; }

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

    public ICommand? AddToSceneCommand { get; }

    public ICommand? ReconnectCommand { get; }
}

public sealed class SceneInspectorViewModel : InspectorPageViewModel
{
    public SceneInspectorViewModel(StudioScene scene, IEnumerable<StudioOutput> linkedOutputs)
        : base(StudioSelectionKind.Scene, scene.DisplayName, scene.IsProgram ? "Cena principal" : "Cena em edicao", "SCN")
    {
        CanvasSize = $"{scene.Canvas.Width:0} x {scene.Canvas.Height:0}";
        AspectRatio = scene.Canvas.Width / Math.Max(1, scene.Canvas.Height) > 1.7 ? "16:9" : "Personalizado";
        LinkedOutputs = string.Join(", ", linkedOutputs.Select(output => output.DisplayName));
        if (string.IsNullOrWhiteSpace(LinkedOutputs))
        {
            LinkedOutputs = "Nenhuma saida vinculada";
        }

        LayerCount = scene.Layers.Count;
        FrameRateText = $"{scene.Canvas.FrameRate:0.##} fps";
        BackgroundColor = scene.Canvas.BackgroundColor;
        IsProgram = scene.IsProgram;
    }

    public string CanvasSize { get; }

    public string AspectRatio { get; }

    public string LinkedOutputs { get; }

    public int LayerCount { get; }

    public string FrameRateText { get; }

    public string BackgroundColor { get; }

    public bool IsProgram { get; }
}

public sealed class OutputInspectorViewModel : InspectorPageViewModel
{
    private readonly StudioOutput _output;
    private readonly Action _onRouteChanged;

    public OutputInspectorViewModel(StudioOutput output, IEnumerable<StudioScene> scenes, Action onRouteChanged)
        : base(StudioSelectionKind.Output, output.DisplayName, "Saida roteada para cena", "OUT")
    {
        _output = output;
        _onRouteChanged = onRouteChanged;
        Scenes = new ObservableCollection<SceneRouteOptionViewModel>(
            scenes.Select(scene => new SceneRouteOptionViewModel(scene.Id, scene.DisplayName)));
        OutputTypes = new ObservableCollection<string> { "Preview", "Gravacao MP4", "Transmissao RTMP", "Camera virtual" };
        CodecOptions = new ObservableCollection<string> { "H.264 (NVENC)", "H.264", "HEVC", "RGBA" };
        PresetOptions = new ObservableCollection<string> { "Qualidade", "Balanceado", "Baixa latencia" };
        SelectedOutputType = new StudioDisplayNameService().GetOutputTypeName(output.TypeId);
        SelectedCodec = string.IsNullOrWhiteSpace(output.Codec) ? CodecOptions[0] : output.Codec;
        SelectedPreset = PresetOptions[0];
    }

    public string Destination
    {
        get => _output.Destination;
        set
        {
            if (_output.Destination != value)
            {
                _output.Destination = value;
                OnPropertyChanged();
            }
        }
    }

    public string Codec
    {
        get => _output.Codec;
        set
        {
            if (_output.Codec != value)
            {
                _output.Codec = value;
                OnPropertyChanged();
            }
        }
    }

    public string Bitrate
    {
        get => _output.Bitrate;
        set
        {
            if (_output.Bitrate != value)
            {
                _output.Bitrate = value;
                OnPropertyChanged();
            }
        }
    }

    public string Health => _output.IsConfigured ? "Configurada" : "Falta configurar";

    public bool IsEnabled
    {
        get => _output.IsEnabled;
        set
        {
            if (_output.IsEnabled != value)
            {
                _output.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsConfigured
    {
        get => _output.IsConfigured;
        set
        {
            if (_output.IsConfigured != value)
            {
                _output.IsConfigured = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Health));
                _onRouteChanged();
            }
        }
    }

    public string SelectedSceneId
    {
        get => _output.AssignedSceneId;
        set
        {
            if (_output.AssignedSceneId != value)
            {
                _output.AssignedSceneId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSceneName));
                OnPropertyChanged(nameof(SelectedScene));
                _onRouteChanged();
            }
        }
    }

    public string SelectedSceneName => Scenes.FirstOrDefault(scene => scene.Id == SelectedSceneId)?.Name ?? "Sem cena";

    public SceneRouteOptionViewModel? SelectedScene
    {
        get => Scenes.FirstOrDefault(scene => scene.Id == SelectedSceneId);
        set
        {
            if (value is not null)
            {
                SelectedSceneId = value.Id;
                OnPropertyChanged();
            }
        }
    }

    public string MaskedStreamKey => string.IsNullOrWhiteSpace(_output.Secret) ? "Nao configurada" : "sk_live_************";

    public ObservableCollection<SceneRouteOptionViewModel> Scenes { get; }

    public ObservableCollection<string> OutputTypes { get; }

    public ObservableCollection<string> CodecOptions { get; }

    public ObservableCollection<string> PresetOptions { get; }

    public string SelectedOutputType { get; set; }

    public string SelectedCodec { get; set; }

    public string SelectedPreset { get; set; }

    public int KeyframeSeconds { get; set; } = 2;

    internal string RawStreamKeyForTests => _output.Secret;
}

public sealed class SceneRouteOptionViewModel
{
    public SceneRouteOptionViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
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
        Items = "Cenas, presets e definicoes de fonte";
        ImportMode = "Validacao antes de importar";
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
