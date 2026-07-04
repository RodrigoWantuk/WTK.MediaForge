using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class StudioShellViewModel : ViewModelBase
{
    private ProjectTreeItemViewModel? _selectedProjectItem;
    private LayerItemViewModel? _selectedLayer;
    private bool _isEngineRunning;
    private bool _isStreaming;
    private bool _isRecording;

    public StudioShellViewModel()
    {
        NewProjectCommand = new RelayCommand(() => LogAction("INFO", "Created a new mock project shell."));
        OpenProjectCommand = new AsyncRelayCommand(() => RunFakeAsync("Opened project package."));
        SaveProjectCommand = new AsyncRelayCommand(() => RunFakeAsync("Saved project package."));
        AddSourceCommand = new RelayCommand(() => LogAction("INFO", "Prepared Add Source dialog state."));
        AddSceneCommand = new RelayCommand(() => LogAction("INFO", "Added empty scene placeholder."));
        SettingsCommand = new RelayCommand(() => LogAction("INFO", "Opened settings placeholder."));
        ToggleEngineCommand = new RelayCommand(ToggleEngine);
        ToggleStreamingCommand = new RelayCommand(ToggleStreaming, () => IsEngineRunning);
        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => IsEngineRunning);
        SelectProjectItemCommand = new RelayCommand<ProjectTreeItemViewModel>(SelectProjectItem, item => item is not null);
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(SelectLayer, layer => layer is not null);
        ToggleLayerVisibilityCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerVisibility, layer => layer is not null);
        ToggleLayerLockCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerLock, layer => layer is not null);
        ToggleEffectEnabledCommand = new RelayCommand<EffectItemViewModel>(ToggleEffectEnabled, effect => effect is not null);
        ReconnectSourceCommand = new RelayCommand(() => LogAction("INFO", "Queued mock source reconnect."));
    }

    public TitleBarViewModel TitleBar { get; } = new();

    public ToolbarViewModel Toolbar { get; } = new();

    public ProjectExplorerViewModel ProjectExplorer { get; } = new();

    public PreviewCanvasViewModel Preview { get; } = new();

    public InspectorHostViewModel Inspector { get; } = new();

    public BottomWorkbenchViewModel BottomWorkbench { get; } = new();

    public StatusBarViewModel StatusBar { get; } = new();

    public ICommand NewProjectCommand { get; }

    public ICommand OpenProjectCommand { get; }

    public ICommand SaveProjectCommand { get; }

    public ICommand AddSourceCommand { get; }

    public ICommand AddSceneCommand { get; }

    public ICommand SettingsCommand { get; }

    public IRelayCommand ToggleEngineCommand { get; }

    public IRelayCommand ToggleStreamingCommand { get; }

    public IRelayCommand ToggleRecordingCommand { get; }

    public IRelayCommand<ProjectTreeItemViewModel> SelectProjectItemCommand { get; }

    public IRelayCommand<LayerItemViewModel> SelectLayerCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerVisibilityCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerLockCommand { get; }

    public IRelayCommand<EffectItemViewModel> ToggleEffectEnabledCommand { get; }

    public ICommand ReconnectSourceCommand { get; }

    public bool IsEngineRunning
    {
        get => _isEngineRunning;
        private set
        {
            if (SetProperty(ref _isEngineRunning, value))
            {
                ToggleStreamingCommand.NotifyCanExecuteChanged();
                ToggleRecordingCommand.NotifyCanExecuteChanged();
                UpdateEngineStateText();
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set
        {
            if (SetProperty(ref _isStreaming, value))
            {
                Toolbar.StreamButtonText = value ? "Stop Stream" : "Start Stream";
                UpdateOutputStatus();
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                Toolbar.RecordingButtonText = value ? "Stop Recording" : "Start Recording";
                UpdateOutputStatus();
            }
        }
    }

    public ProjectTreeItemViewModel? SelectedProjectItem
    {
        get => _selectedProjectItem;
        private set => SetProperty(ref _selectedProjectItem, value);
    }

    public LayerItemViewModel? SelectedLayer
    {
        get => _selectedLayer;
        private set => SetProperty(ref _selectedLayer, value);
    }

    public void LoadDesignData(
        IEnumerable<ProjectTreeGroupViewModel> projectGroups,
        IEnumerable<LayerItemViewModel> layers,
        IEnumerable<EffectItemViewModel> effects,
        IEnumerable<DiagnosticLogItemViewModel> diagnostics,
        IEnumerable<PerformanceMetricViewModel> performanceMetrics,
        IEnumerable<OutputMonitorItemViewModel> outputs,
        IEnumerable<AudioStripViewModel> audioStrips)
    {
        Replace(ProjectExplorer.Groups, projectGroups);
        Replace(BottomWorkbench.Layers, layers);
        Replace(BottomWorkbench.Effects, effects);
        Replace(BottomWorkbench.Diagnostics, diagnostics);
        Replace(BottomWorkbench.PerformanceMetrics, performanceMetrics);
        Replace(BottomWorkbench.Outputs, outputs);
        Replace(BottomWorkbench.AudioStrips, audioStrips);

        Replace(
            BottomWorkbench.Tabs,
            new[]
            {
                new BottomTabViewModel(StudioBottomTabKind.Layers, "Layers"),
                new BottomTabViewModel(StudioBottomTabKind.Effects, "Effects"),
                new BottomTabViewModel(StudioBottomTabKind.Timeline, "Timeline"),
                new BottomTabViewModel(StudioBottomTabKind.Diagnostics, "Diagnostics", "5"),
                new BottomTabViewModel(StudioBottomTabKind.Performance, "Performance"),
                new BottomTabViewModel(StudioBottomTabKind.OutputMonitor, "Output Monitor"),
                new BottomTabViewModel(StudioBottomTabKind.AudioMixer, "Audio Mixer", "BETA")
            });

        AttachCommands();
        BottomWorkbench.SelectTab(BottomWorkbench.Tabs[0]);

        var mainScene = ProjectExplorer.Groups
            .SelectMany(group => group.Items)
            .FirstOrDefault(item => item.Kind == StudioProjectItemKind.Scene);

        if (mainScene is not null)
        {
            SelectProjectItem(mainScene);
        }

        var selectedLayer = BottomWorkbench.Layers.FirstOrDefault(layer => layer.Name == "Lower Third")
            ?? BottomWorkbench.Layers.FirstOrDefault();

        if (selectedLayer is not null)
        {
            SelectLayer(selectedLayer);
        }
    }

    public void SelectProjectItem(ProjectTreeItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        ClearProjectSelection();
        item.IsSelected = true;
        SelectedProjectItem = item;

        Inspector.SelectedPage = item.Kind switch
        {
            StudioProjectItemKind.Scene => new SceneInspectorViewModel(item.Name, item.Name == "Main Scene" ? "Preview, Recording MP4, RTMP Twitch" : "Preview"),
            StudioProjectItemKind.Source => CreateSourceInspector(item),
            StudioProjectItemKind.Output => CreateOutputInspector(item),
            StudioProjectItemKind.Preset => new PresetInspectorViewModel(item.Name, item.Metadata),
            StudioProjectItemKind.Package => new PackageInspectorViewModel(item.Name, item.Metadata),
            _ => new EmptyInspectorViewModel()
        };

        StatusBar.StatusText = $"Selected {item.Name}";
        Preview.SceneName = item.Kind == StudioProjectItemKind.Scene ? item.Name : Preview.SceneName;
    }

    public void SelectLayer(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        foreach (var item in BottomWorkbench.Layers)
        {
            item.IsSelected = ReferenceEquals(item, layer);
        }

        SelectedLayer = layer;
        Preview.SelectedLayerName = layer.Name;
        Inspector.SelectedPage = new LayerInspectorViewModel(layer.Name, layer.Source);
        StatusBar.StatusText = $"Selected layer {layer.Name}";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void AttachCommands()
    {
        foreach (var item in ProjectExplorer.Groups.SelectMany(group => group.Items))
        {
            item.SelectCommand = SelectProjectItemCommand;
        }

        foreach (var layer in BottomWorkbench.Layers)
        {
            layer.SelectCommand = SelectLayerCommand;
            layer.ToggleVisibilityCommand = ToggleLayerVisibilityCommand;
            layer.ToggleLockCommand = ToggleLayerLockCommand;
        }

        foreach (var effect in BottomWorkbench.Effects)
        {
            effect.ToggleEnabledCommand = ToggleEffectEnabledCommand;
        }

        foreach (var tab in BottomWorkbench.Tabs)
        {
            tab.SelectCommand = BottomWorkbench.SelectTabCommand;
        }
    }

    private void ToggleEngine()
    {
        IsEngineRunning = !IsEngineRunning;

        if (!IsEngineRunning)
        {
            IsStreaming = false;
            IsRecording = false;
        }

        LogAction("INFO", IsEngineRunning ? "Mock engine started." : "Mock engine stopped.");
    }

    private void ToggleStreaming()
    {
        if (!IsEngineRunning)
        {
            return;
        }

        IsStreaming = !IsStreaming;
        LogAction(IsStreaming ? "LIVE" : "INFO", IsStreaming ? "Mock RTMP stream marked live." : "Mock RTMP stream stopped.");
    }

    private void ToggleRecording()
    {
        if (!IsEngineRunning)
        {
            return;
        }

        IsRecording = !IsRecording;
        LogAction(IsRecording ? "REC" : "INFO", IsRecording ? "Mock MP4 recording started." : "Mock MP4 recording stopped.");
    }

    private void ToggleLayerVisibility(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsVisible = !layer.IsVisible;
        LogAction("INFO", $"{layer.Name} visibility set to {layer.IsVisible}.");
    }

    private void ToggleLayerLock(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsLocked = !layer.IsLocked;
        LogAction("INFO", $"{layer.Name} lock set to {layer.IsLocked}.");
    }

    private void ToggleEffectEnabled(EffectItemViewModel? effect)
    {
        if (effect is null)
        {
            return;
        }

        effect.IsEnabled = !effect.IsEnabled;
        LogAction("INFO", $"{effect.Name} enabled set to {effect.IsEnabled}.");
    }

    private async Task RunFakeAsync(string message)
    {
        await Task.Delay(10).ConfigureAwait(true);
        LogAction("INFO", message);
    }

    private void ClearProjectSelection()
    {
        foreach (var item in ProjectExplorer.Groups.SelectMany(group => group.Items))
        {
            item.IsSelected = false;
        }
    }

    private SourceInspectorViewModel CreateSourceInspector(ProjectTreeItemViewModel item)
    {
        var endpoint = item.Name switch
        {
            "Webcam" => "Logitech BRIO / Device 0",
            "Desktop Capture" => "Display 1 / Desktop duplication",
            "Logo.png" => "assets/brand/Logo.png",
            "Lower Third" => "Text template / Brand Kit",
            "Intro.mp4" => "media/intro.mp4",
            _ => item.Metadata
        };

        return new SourceInspectorViewModel(item.Name, item.Metadata, endpoint)
        {
            ReconnectCommand = ReconnectSourceCommand
        };
    }

    private static OutputInspectorViewModel CreateOutputInspector(ProjectTreeItemViewModel item)
    {
        return item.Name switch
        {
            "Recording MP4" => new OutputInspectorViewModel(item.Name, "D:/captures/session.mp4", "H.264", "18 Mb/s", ""),
            "RTMP Twitch" => new OutputInspectorViewModel(item.Name, "rtmp://live.twitch.tv/app", "H.264", "6 Mb/s", "sk_live_2d97c8a6_raw_secret"),
            "Virtual Camera" => new OutputInspectorViewModel(item.Name, "Virtual camera device", "NV12", "60 fps", ""),
            _ => new OutputInspectorViewModel(item.Name, "Local preview panel", "RGBA", "GPU surface", "")
        };
    }

    private void UpdateEngineStateText()
    {
        Toolbar.EngineButtonText = IsEngineRunning ? "Stop Engine" : "Start Engine";
        Toolbar.StateBadge = IsEngineRunning ? "Mock engine running" : "Mock mode";
        TitleBar.EngineState = IsEngineRunning ? "Engine running" : "Engine stopped";
        StatusBar.EngineText = IsEngineRunning ? "Running" : "Stopped";
        StatusBar.GpuText = IsEngineRunning ? "GPU mock 31%" : "GPU mock idle";
    }

    private void UpdateOutputStatus()
    {
        StatusBar.OutputText = (IsStreaming, IsRecording) switch
        {
            (true, true) => "Live + Recording",
            (true, false) => "Live",
            (false, true) => "Recording",
            _ => "Preview idle"
        };
    }

    private void LogAction(string level, string message)
    {
        BottomWorkbench.AddDiagnostic(level, message);
        StatusBar.StatusText = message;
    }
}
