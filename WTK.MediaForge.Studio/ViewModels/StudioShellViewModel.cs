using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class StudioShellViewModel : ViewModelBase
{
    private readonly IStudioProjectService _projectService;
    private readonly IStudioEngineService _engineService;
    private readonly IStudioOutputService _outputService;
    private readonly IStudioDiagnosticsService _diagnosticsService;
    private readonly IStudioSelectionService _selectionService;
    private readonly IInspectorPageFactory _inspectorPageFactory;
    private ProjectTreeItemViewModel? _selectedProjectItem;
    private LayerItemViewModel? _selectedLayer;
    private StudioSelectionState _currentSelection = StudioSelectionState.None;

    public StudioShellViewModel()
        : this(StudioServiceFactory.CreateFake())
    {
    }

    public StudioShellViewModel(StudioServiceBundle services)
        : this(
            services.ProjectService,
            services.EngineService,
            services.OutputService,
            services.DiagnosticsService,
            services.SelectionService,
            services.InspectorPageFactory)
    {
    }

    public StudioShellViewModel(
        IStudioProjectService projectService,
        IStudioEngineService engineService,
        IStudioOutputService outputService,
        IStudioDiagnosticsService diagnosticsService,
        IStudioSelectionService selectionService,
        IInspectorPageFactory inspectorPageFactory)
    {
        _projectService = projectService;
        _engineService = engineService;
        _outputService = outputService;
        _diagnosticsService = diagnosticsService;
        _selectionService = selectionService;
        _inspectorPageFactory = inspectorPageFactory;

        BottomWorkbench = new BottomWorkbenchViewModel(_diagnosticsService.Items);

        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        AddSourceCommand = new RelayCommand(() => LogAction("INFO", "Project", "Prepared Add Source dialog state."));
        AddSceneCommand = new RelayCommand(() => LogAction("INFO", "Project", "Added empty scene placeholder."));
        SettingsCommand = new RelayCommand(() => LogAction("INFO", "Studio", "Opened settings placeholder."));
        ToggleEngineCommand = new AsyncRelayCommand(ToggleEngineAsync, CanToggleEngine);
        ToggleStreamingCommand = new AsyncRelayCommand(ToggleStreamingAsync, CanToggleStreaming);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        SelectProjectItemCommand = new RelayCommand<ProjectTreeItemViewModel>(SelectProjectItem, item => item is not null);
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(SelectLayer, layer => layer is not null);
        ToggleLayerVisibilityCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerVisibility, layer => layer is not null);
        ToggleLayerLockCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerLock, layer => layer is not null);
        ToggleEffectEnabledCommand = new RelayCommand<EffectItemViewModel>(ToggleEffectEnabled, effect => effect is not null);
        ReconnectSourceCommand = new RelayCommand(() => LogAction("INFO", "Source", "Queued mock source reconnect."));

        _engineService.StatusChanged += OnEngineStatusChanged;
        _outputService.StatusChanged += OnOutputStatusChanged;
        _selectionService.SelectionChanged += OnSelectionChanged;

        ApplyProjectDocument();
        ApplyEngineStatus(_engineService.CurrentStatus);
        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
    }

    public TitleBarViewModel TitleBar { get; } = new();

    public ToolbarViewModel Toolbar { get; } = new();

    public ProjectExplorerViewModel ProjectExplorer { get; } = new();

    public PreviewCanvasViewModel Preview { get; } = new();

    public InspectorHostViewModel Inspector { get; } = new();

    public BottomWorkbenchViewModel BottomWorkbench { get; }

    public StatusBarViewModel StatusBar { get; } = new();

    public IAsyncRelayCommand NewProjectCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IAsyncRelayCommand SaveProjectCommand { get; }

    public ICommand AddSourceCommand { get; }

    public ICommand AddSceneCommand { get; }

    public ICommand SettingsCommand { get; }

    public IAsyncRelayCommand ToggleEngineCommand { get; }

    public IAsyncRelayCommand ToggleStreamingCommand { get; }

    public IAsyncRelayCommand ToggleRecordingCommand { get; }

    public IRelayCommand<ProjectTreeItemViewModel> SelectProjectItemCommand { get; }

    public IRelayCommand<LayerItemViewModel> SelectLayerCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerVisibilityCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerLockCommand { get; }

    public IRelayCommand<EffectItemViewModel> ToggleEffectEnabledCommand { get; }

    public ICommand ReconnectSourceCommand { get; }

    public bool IsEngineRunning => _engineService.CurrentStatus.State == StudioEngineUiState.Running;

    public bool IsStreaming => _outputService.StreamingState == StudioOutputUiState.Running;

    public bool IsRecording => _outputService.RecordingState == StudioOutputUiState.Running;

    public StudioSelectionState CurrentSelection
    {
        get => _currentSelection;
        private set => SetProperty(ref _currentSelection, value);
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
        Replace(_diagnosticsService.Items, diagnostics);
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

        ClearLayerSelection();
        ClearProjectSelection();
        item.IsSelected = true;
        SelectedProjectItem = item;
        SelectedLayer = null;
        _selectionService.Select(CreateSelection(item));
    }

    public void SelectLayer(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        ClearProjectSelection();
        foreach (var item in BottomWorkbench.Layers)
        {
            item.IsSelected = ReferenceEquals(item, layer);
        }

        SelectedProjectItem = null;
        SelectedLayer = layer;
        Preview.SelectedLayerName = layer.Name;
        _selectionService.Select(new StudioSelectionState(
            StudioSelectionKind.Layer,
            layer.Id,
            layer.Name,
            layer.Type,
            layer.Type,
            layer.Source));
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

    private async Task NewProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.NewAsync(cancellationToken).ConfigureAwait(true);
        ApplyProjectDocument();
        LogAction("INFO", "Project", "Created a new mock project shell.");
    }

    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.OpenAsync("mock-project.mforge.json", cancellationToken).ConfigureAwait(true);
        ApplyProjectDocument();
        LogAction("INFO", "Project", "Opened project package.");
    }

    private async Task SaveProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.SaveAsync(null, cancellationToken).ConfigureAwait(true);
        ApplyProjectDocument();
        LogAction("INFO", "Project", "Saved project package.");
    }

    private bool CanToggleEngine()
    {
        return _engineService.CurrentStatus.State is StudioEngineUiState.Stopped or StudioEngineUiState.Running or StudioEngineUiState.Failed;
    }

    private bool CanToggleStreaming()
    {
        return CanToggleOutput(_outputService.StreamingState);
    }

    private bool CanToggleRecording()
    {
        return CanToggleOutput(_outputService.RecordingState);
    }

    private bool CanToggleOutput(StudioOutputUiState state)
    {
        return _engineService.CurrentStatus.State == StudioEngineUiState.Running
            && state is not StudioOutputUiState.Starting
            && state is not StudioOutputUiState.Stopping
            && state is not StudioOutputUiState.Planned
            && state is not StudioOutputUiState.NotConfigured;
    }

    private async Task ToggleEngineAsync(CancellationToken cancellationToken)
    {
        if (_engineService.CurrentStatus.State == StudioEngineUiState.Running)
        {
            await _outputService.StopAllAsync(cancellationToken).ConfigureAwait(true);
            await _engineService.StopAsync(cancellationToken).ConfigureAwait(true);
            LogAction("INFO", "Engine", "Mock engine stopped.");
            return;
        }

        await _engineService.StartAsync(cancellationToken).ConfigureAwait(true);
        LogAction("INFO", "Engine", "Mock engine started.");
    }

    private async Task ToggleStreamingAsync(CancellationToken cancellationToken)
    {
        if (!CanToggleStreaming())
        {
            return;
        }

        await _outputService.ToggleStreamingAsync(cancellationToken).ConfigureAwait(true);
        LogAction(IsStreaming ? "LIVE" : "INFO", "Output", IsStreaming ? "Mock RTMP stream marked live." : "Mock RTMP stream stopped.");
    }

    private async Task ToggleRecordingAsync(CancellationToken cancellationToken)
    {
        if (!CanToggleRecording())
        {
            return;
        }

        await _outputService.ToggleRecordingAsync(cancellationToken).ConfigureAwait(true);
        LogAction(IsRecording ? "REC" : "INFO", "Output", IsRecording ? "Mock MP4 recording started." : "Mock MP4 recording stopped.");
    }

    private void ToggleLayerVisibility(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsVisible = !layer.IsVisible;
        LogAction("INFO", "Layer", $"{layer.Name} visibility set to {layer.VisibilityGlyph}.");
    }

    private void ToggleLayerLock(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsLocked = !layer.IsLocked;
        LogAction("INFO", "Layer", $"{layer.Name} lock set to {layer.LockGlyph}.");
    }

    private void ToggleEffectEnabled(EffectItemViewModel? effect)
    {
        if (effect is null)
        {
            return;
        }

        effect.IsEnabled = !effect.IsEnabled;
        LogAction("INFO", "Effect", $"{effect.Name} enabled set to {effect.IsEnabled}.");
    }

    private void ClearProjectSelection()
    {
        foreach (var item in ProjectExplorer.Groups.SelectMany(group => group.Items))
        {
            item.IsSelected = false;
        }
    }

    private void ClearLayerSelection()
    {
        foreach (var layer in BottomWorkbench.Layers)
        {
            layer.IsSelected = false;
        }
    }

    private static StudioSelectionState CreateSelection(ProjectTreeItemViewModel item)
    {
        var kind = item.Kind switch
        {
            StudioProjectItemKind.Scene => StudioSelectionKind.Scene,
            StudioProjectItemKind.Source => StudioSelectionKind.Source,
            StudioProjectItemKind.Output => StudioSelectionKind.Output,
            StudioProjectItemKind.Preset => StudioSelectionKind.Preset,
            StudioProjectItemKind.Package => StudioSelectionKind.Package,
            _ => StudioSelectionKind.None
        };

        return new StudioSelectionState(
            kind,
            item.Id,
            item.Name,
            item.TypeId,
            item.Metadata,
            item.Detail,
            item.Destination,
            item.Codec,
            item.Bitrate,
            item.Secret);
    }

    private void ApplyProjectDocument()
    {
        TitleBar.ProjectName = _projectService.Current.HasUnsavedChanges
            ? $"{_projectService.Current.DisplayName} *"
            : _projectService.Current.DisplayName;
    }

    private void OnEngineStatusChanged(object? sender, StudioEngineStatusChangedEventArgs e)
    {
        ApplyEngineStatus(e.Status);
    }

    private void ApplyEngineStatus(StudioEngineStatus status)
    {
        Toolbar.EngineState = status.State;
        Toolbar.EngineButtonText = status.State switch
        {
            StudioEngineUiState.Starting => "Starting...",
            StudioEngineUiState.Running => "Stop Engine",
            StudioEngineUiState.Stopping => "Stopping...",
            StudioEngineUiState.Failed => "Restart Engine",
            _ => "Start Engine"
        };
        Toolbar.StateBadge = status.State switch
        {
            StudioEngineUiState.Running => "Mock engine running",
            StudioEngineUiState.Starting => "Mock engine starting",
            StudioEngineUiState.Stopping => "Mock engine stopping",
            StudioEngineUiState.Failed => "Mock engine failed",
            _ => "Mock mode"
        };
        TitleBar.EngineState = status.Message;
        StatusBar.EngineText = status.State.ToString();
        StatusBar.GpuText = status.State == StudioEngineUiState.Running ? "GPU mock 31%" : "GPU mock idle";

        OnPropertyChanged(nameof(IsEngineRunning));
        ToggleEngineCommand.NotifyCanExecuteChanged();
        ToggleStreamingCommand.NotifyCanExecuteChanged();
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    private void OnOutputStatusChanged(object? sender, StudioOutputStatusChangedEventArgs e)
    {
        ApplyOutputState(e.StreamingState, e.RecordingState);
    }

    private void ApplyOutputState(StudioOutputUiState streamingState, StudioOutputUiState recordingState)
    {
        Toolbar.StreamingState = streamingState;
        Toolbar.RecordingState = recordingState;
        Toolbar.StreamButtonText = streamingState switch
        {
            StudioOutputUiState.Starting => "Connecting...",
            StudioOutputUiState.Running => "Live",
            StudioOutputUiState.Stopping => "Stopping...",
            StudioOutputUiState.Error => "Stream Error",
            StudioOutputUiState.Planned => "Stream Planned",
            _ => "Start Streaming"
        };
        Toolbar.RecordingButtonText = recordingState switch
        {
            StudioOutputUiState.Starting => "Recording...",
            StudioOutputUiState.Running => "Recording 00:00:00",
            StudioOutputUiState.Stopping => "Stopping...",
            StudioOutputUiState.Error => "Record Error",
            StudioOutputUiState.Planned => "Record Planned",
            _ => "Start Recording"
        };
        StatusBar.OutputText = (streamingState == StudioOutputUiState.Running, recordingState == StudioOutputUiState.Running) switch
        {
            (true, true) => "Live + Recording",
            (true, false) => "Live",
            (false, true) => "Recording",
            _ => "Preview idle"
        };

        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(IsRecording));
        ToggleStreamingCommand.NotifyCanExecuteChanged();
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged(object? sender, StudioSelectionChangedEventArgs e)
    {
        CurrentSelection = e.Selection;
        Inspector.SelectedPage = _inspectorPageFactory.Create(e.Selection, ReconnectSourceCommand);
        StatusBar.StatusText = e.Selection.Kind == StudioSelectionKind.Layer
            ? $"Selected layer {e.Selection.DisplayName}"
            : $"Selected {e.Selection.DisplayName}";

        if (e.Selection.Kind == StudioSelectionKind.Scene)
        {
            Preview.SceneName = e.Selection.DisplayName;
        }
    }

    private void LogAction(string level, string category, string message)
    {
        _diagnosticsService.Append(level, category, message);
        StatusBar.StatusText = message;
    }
}
