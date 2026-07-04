using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class TitleBarViewModel : ViewModelBase
{
    private string _engineState = "Engine stopped";

    public string ProductName { get; } = "WTK MediaForge Studio";

    public string ProjectName { get; } = "Live Production Workspace";

    public string EngineState
    {
        get => _engineState;
        set => SetProperty(ref _engineState, value);
    }
}

public sealed class ToolbarViewModel : ViewModelBase
{
    private string _engineButtonText = "Start Engine";
    private string _streamButtonText = "Start Stream";
    private string _recordingButtonText = "Start Recording";
    private string _stateBadge = "Mock mode";

    public string EngineButtonText
    {
        get => _engineButtonText;
        set => SetProperty(ref _engineButtonText, value);
    }

    public string StreamButtonText
    {
        get => _streamButtonText;
        set => SetProperty(ref _streamButtonText, value);
    }

    public string RecordingButtonText
    {
        get => _recordingButtonText;
        set => SetProperty(ref _recordingButtonText, value);
    }

    public string StateBadge
    {
        get => _stateBadge;
        set => SetProperty(ref _stateBadge, value);
    }
}

public sealed class ProjectExplorerViewModel : ViewModelBase
{
    public ObservableCollection<ProjectTreeGroupViewModel> Groups { get; } = new();
}

public sealed class PreviewCanvasViewModel : ViewModelBase
{
    private bool _isGridVisible = true;
    private bool _isSafeFrameVisible = true;
    private string _selectedLayerName = "Lower Third";
    private string _timingLabel = "16.6 ms";

    public string SceneName { get; set; } = "Main Scene";

    public string CanvasSize { get; set; } = "1920 x 1080";

    public string FrameRate { get; set; } = "60 fps";

    public string ZoomLabel { get; set; } = "82%";

    public ICommand ToggleGridCommand { get; }

    public ICommand ToggleSafeFrameCommand { get; }

    public PreviewCanvasViewModel()
    {
        ToggleGridCommand = new RelayCommand(() => IsGridVisible = !IsGridVisible);
        ToggleSafeFrameCommand = new RelayCommand(() => IsSafeFrameVisible = !IsSafeFrameVisible);
    }

    public bool IsGridVisible
    {
        get => _isGridVisible;
        set => SetProperty(ref _isGridVisible, value);
    }

    public bool IsSafeFrameVisible
    {
        get => _isSafeFrameVisible;
        set => SetProperty(ref _isSafeFrameVisible, value);
    }

    public string SelectedLayerName
    {
        get => _selectedLayerName;
        set => SetProperty(ref _selectedLayerName, value);
    }

    public string TimingLabel
    {
        get => _timingLabel;
        set => SetProperty(ref _timingLabel, value);
    }
}

public sealed class BottomWorkbenchViewModel : ViewModelBase
{
    private BottomTabViewModel? _selectedTab;
    private bool _isLayersSelected;
    private bool _isEffectsSelected;
    private bool _isTimelineSelected;
    private bool _isDiagnosticsSelected;
    private bool _isPerformanceSelected;
    private bool _isOutputMonitorSelected;
    private bool _isAudioMixerSelected;

    public BottomWorkbenchViewModel()
    {
        SelectTabCommand = new RelayCommand<BottomTabViewModel>(SelectTab, tab => tab is not null);
    }

    public ObservableCollection<BottomTabViewModel> Tabs { get; } = new();

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();

    public ObservableCollection<DiagnosticLogItemViewModel> Diagnostics { get; } = new();

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new();

    public ObservableCollection<OutputMonitorItemViewModel> Outputs { get; } = new();

    public ObservableCollection<AudioStripViewModel> AudioStrips { get; } = new();

    public IRelayCommand<BottomTabViewModel> SelectTabCommand { get; }

    public BottomTabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set => SetProperty(ref _selectedTab, value);
    }

    public bool IsLayersSelected
    {
        get => _isLayersSelected;
        private set => SetProperty(ref _isLayersSelected, value);
    }

    public bool IsEffectsSelected
    {
        get => _isEffectsSelected;
        private set => SetProperty(ref _isEffectsSelected, value);
    }

    public bool IsTimelineSelected
    {
        get => _isTimelineSelected;
        private set => SetProperty(ref _isTimelineSelected, value);
    }

    public bool IsDiagnosticsSelected
    {
        get => _isDiagnosticsSelected;
        private set => SetProperty(ref _isDiagnosticsSelected, value);
    }

    public bool IsPerformanceSelected
    {
        get => _isPerformanceSelected;
        private set => SetProperty(ref _isPerformanceSelected, value);
    }

    public bool IsOutputMonitorSelected
    {
        get => _isOutputMonitorSelected;
        private set => SetProperty(ref _isOutputMonitorSelected, value);
    }

    public bool IsAudioMixerSelected
    {
        get => _isAudioMixerSelected;
        private set => SetProperty(ref _isAudioMixerSelected, value);
    }

    public void SelectTab(BottomTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SelectedTab = tab;
        IsLayersSelected = tab.Kind == StudioBottomTabKind.Layers;
        IsEffectsSelected = tab.Kind == StudioBottomTabKind.Effects;
        IsTimelineSelected = tab.Kind == StudioBottomTabKind.Timeline;
        IsDiagnosticsSelected = tab.Kind == StudioBottomTabKind.Diagnostics;
        IsPerformanceSelected = tab.Kind == StudioBottomTabKind.Performance;
        IsOutputMonitorSelected = tab.Kind == StudioBottomTabKind.OutputMonitor;
        IsAudioMixerSelected = tab.Kind == StudioBottomTabKind.AudioMixer;
    }

    public void AddDiagnostic(string level, string message)
    {
        Diagnostics.Insert(0, new DiagnosticLogItemViewModel(DateTime.Now.ToString("HH:mm:ss"), level, message));

        while (Diagnostics.Count > 80)
        {
            Diagnostics.RemoveAt(Diagnostics.Count - 1);
        }
    }
}

public sealed class StatusBarViewModel : ViewModelBase
{
    private string _statusText = "Ready";
    private string _engineText = "Stopped";
    private string _framesText = "0 dropped";
    private string _gpuText = "GPU mock idle";
    private string _outputText = "Preview idle";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string EngineText
    {
        get => _engineText;
        set => SetProperty(ref _engineText, value);
    }

    public string FramesText
    {
        get => _framesText;
        set => SetProperty(ref _framesText, value);
    }

    public string GpuText
    {
        get => _gpuText;
        set => SetProperty(ref _gpuText, value);
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }
}
