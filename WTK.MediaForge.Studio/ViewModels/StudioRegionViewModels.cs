using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class TitleBarViewModel : ViewModelBase
{
    private string _engineState = "Engine stopped";
    private string _projectName = "Live Production Workspace";

    public string ProductName { get; } = "WTK MediaForge Studio";

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

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
    private string _stateBadge = "Preview mode";
    private StudioEngineUiState _engineState = StudioEngineUiState.Stopped;
    private StudioOutputUiState _streamingState = StudioOutputUiState.Ready;
    private StudioOutputUiState _recordingState = StudioOutputUiState.Ready;

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

    public StudioEngineUiState EngineState
    {
        get => _engineState;
        set
        {
            if (SetProperty(ref _engineState, value))
            {
                OnPropertyChanged(nameof(IsEngineBusy));
                OnPropertyChanged(nameof(EngineButtonClasses));
            }
        }
    }

    public StudioOutputUiState StreamingState
    {
        get => _streamingState;
        set
        {
            if (SetProperty(ref _streamingState, value))
            {
                OnPropertyChanged(nameof(IsStreamBusy));
                OnPropertyChanged(nameof(StreamButtonClasses));
            }
        }
    }

    public StudioOutputUiState RecordingState
    {
        get => _recordingState;
        set
        {
            if (SetProperty(ref _recordingState, value))
            {
                OnPropertyChanged(nameof(IsRecordingBusy));
                OnPropertyChanged(nameof(RecordingButtonClasses));
            }
        }
    }

    public bool IsEngineBusy => EngineState is StudioEngineUiState.Starting or StudioEngineUiState.Stopping;

    public bool IsStreamBusy => StreamingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public bool IsRecordingBusy => RecordingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public string EngineButtonClasses => EngineState switch
    {
        StudioEngineUiState.Running => "danger",
        StudioEngineUiState.Failed => "danger",
        StudioEngineUiState.Starting or StudioEngineUiState.Stopping => "busy",
        _ => "primary"
    };

    public string StreamButtonClasses => StreamingState switch
    {
        StudioOutputUiState.Running => "primary live",
        StudioOutputUiState.Error => "danger",
        StudioOutputUiState.Starting or StudioOutputUiState.Stopping => "busy",
        _ => "primary"
    };

    public string RecordingButtonClasses => RecordingState switch
    {
        StudioOutputUiState.Running => "danger recording",
        StudioOutputUiState.Error => "danger",
        StudioOutputUiState.Starting or StudioOutputUiState.Stopping => "busy",
        _ => "danger"
    };
}

public sealed class ProjectExplorerViewModel : ViewModelBase
{
    private ProjectTreeItemViewModel? _selectedItem;
    private bool _suppressSelectionCommand;

    public ObservableCollection<ProjectTreeGroupViewModel> Groups { get; } = new();

    public ProjectTreeItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value) || _suppressSelectionCommand || value is null)
            {
                return;
            }

            if (value.SelectCommand?.CanExecute(value) == true)
            {
                value.SelectCommand.Execute(value);
            }
        }
    }

    public void SelectFromOwner(ProjectTreeItemViewModel? item)
    {
        _suppressSelectionCommand = true;
        try
        {
            SelectedItem = item;
        }
        finally
        {
            _suppressSelectionCommand = false;
        }
    }
}

public sealed class PreviewCanvasViewModel : ViewModelBase
{
    private bool _isGridVisible = true;
    private bool _isSafeFrameVisible = true;
    private string _selectedLayerName = "Lower Third";
    private string _timingLabel = "16.6 ms";
    private string _sceneName = "Main Scene";
    private string _canvasSize = "1920 x 1080";
    private string _frameRate = "60 fps";
    private string _zoomLabel = "82%";

    public string SceneName
    {
        get => _sceneName;
        set => SetProperty(ref _sceneName, value);
    }

    public string CanvasSize
    {
        get => _canvasSize;
        set => SetProperty(ref _canvasSize, value);
    }

    public string FrameRate
    {
        get => _frameRate;
        set => SetProperty(ref _frameRate, value);
    }

    public string ZoomLabel
    {
        get => _zoomLabel;
        set => SetProperty(ref _zoomLabel, value);
    }

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
    private LayerItemViewModel? _selectedLayer;
    private bool _suppressLayerSelectionCommand;
    private bool _isLayersSelected;
    private bool _isEffectsSelected;
    private bool _isTimelineSelected;
    private bool _isDiagnosticsSelected;
    private bool _isPerformanceSelected;
    private bool _isOutputMonitorSelected;
    private bool _isAudioMixerSelected;

    public BottomWorkbenchViewModel(ObservableCollection<DiagnosticLogItemViewModel>? diagnostics = null)
    {
        Diagnostics = diagnostics ?? new ObservableCollection<DiagnosticLogItemViewModel>();
        SelectTabCommand = new RelayCommand<BottomTabViewModel>(SelectTab, tab => tab is not null);
    }

    public ObservableCollection<BottomTabViewModel> Tabs { get; } = new();

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();

    public ObservableCollection<DiagnosticLogItemViewModel> Diagnostics { get; }

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new();

    public ObservableCollection<OutputMonitorItemViewModel> Outputs { get; } = new();

    public ObservableCollection<AudioStripViewModel> AudioStrips { get; } = new();

    public IRelayCommand<BottomTabViewModel> SelectTabCommand { get; }

    public BottomTabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set => SetProperty(ref _selectedTab, value);
    }

    public LayerItemViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (!SetProperty(ref _selectedLayer, value) || _suppressLayerSelectionCommand || value is null)
            {
                return;
            }

            if (value.SelectCommand?.CanExecute(value) == true)
            {
                value.SelectCommand.Execute(value);
            }
        }
    }

    public void SelectLayerFromOwner(LayerItemViewModel? layer)
    {
        _suppressLayerSelectionCommand = true;
        try
        {
            SelectedLayer = layer;
        }
        finally
        {
            _suppressLayerSelectionCommand = false;
        }
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
    private string _gpuText = "GPU idle";
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
