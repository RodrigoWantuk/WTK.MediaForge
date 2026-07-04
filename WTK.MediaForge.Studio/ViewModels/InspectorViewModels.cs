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
    public LayerInspectorViewModel(string layerName, string sourceName)
        : base(StudioSelectionKind.Layer, layerName, sourceName, "LYR")
    {
        X = "96";
        Y = "684";
        Width = "1,280";
        Height = "148";
        Crop = "0 / 0 / 0 / 0";
        Opacity = 92;
        BlendMode = "Alpha";
        Effects = new ObservableCollection<EffectItemViewModel>
        {
            new("Chroma Key", "Green spill tightened, edge smooth 0.24", true, true),
            new("Blur", "Disabled for this layer", false, false)
        };
    }

    public string X { get; }

    public string Y { get; }

    public string Width { get; }

    public string Height { get; }

    public string Crop { get; }

    public int Opacity { get; }

    public string BlendMode { get; }

    public ObservableCollection<EffectItemViewModel> Effects { get; }
}

public sealed class SourceInspectorViewModel : InspectorPageViewModel
{
    public SourceInspectorViewModel(string sourceName, string sourceType, string endpoint)
        : base(StudioSelectionKind.Source, sourceName, sourceType, "SRC")
    {
        SourceType = sourceType;
        Endpoint = endpoint;
        Resolution = sourceType.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ? "2560 x 1440" : "1920 x 1080";
        FrameRate = sourceType.Contains("Media", StringComparison.OrdinalIgnoreCase) ? "29.97 fps" : "60 fps";
        Status = "Healthy";
    }

    public string SourceType { get; }

    public string Endpoint { get; }

    public string Resolution { get; }

    public string FrameRate { get; }

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
