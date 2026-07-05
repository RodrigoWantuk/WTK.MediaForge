using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Views.Preview;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class TitleBarViewModel : ViewModelBase
{
    private string _projectName = "Produção ao vivo";
    private string _workspaceState = "Pronto";

    public string ProductName { get; } = "WTK MediaForge Studio";

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string WorkspaceState
    {
        get => _workspaceState;
        set => SetProperty(ref _workspaceState, value);
    }
}

public sealed class ToolbarViewModel : ViewModelBase
{
    private string _streamButtonText = "Transmitir";
    private string _recordingButtonText = "Gravar";
    private string _stateBadge = "Pronto";
    private StudioOutputUiState _streamingState = StudioOutputUiState.Ready;
    private StudioOutputUiState _recordingState = StudioOutputUiState.Ready;

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

    public bool IsStreamBusy => StreamingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public bool IsRecordingBusy => RecordingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public string StreamButtonClasses => StreamingState switch
    {
        StudioOutputUiState.Running => "live",
        StudioOutputUiState.Error => "danger",
        StudioOutputUiState.Starting or StudioOutputUiState.Stopping => "busy",
        StudioOutputUiState.NotConfigured => "warning",
        _ => "primary"
    };

    public string RecordingButtonClasses => RecordingState switch
    {
        StudioOutputUiState.Running => "danger recording",
        StudioOutputUiState.Error => "danger",
        StudioOutputUiState.Starting or StudioOutputUiState.Stopping => "busy",
        StudioOutputUiState.NotConfigured => "warning",
        _ => "danger"
    };
}

public sealed class ProjectExplorerViewModel : ViewModelBase
{
    private ProjectTreeItemViewModel? _selectedItem;
    private bool _suppressSelectionCommand;
    private string _searchText = string.Empty;

    public ObservableCollection<ProjectTreeGroupViewModel> Groups { get; } = new();

    public ICommand? AddSceneCommand { get; set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public int VisibleItemCount => Groups.Sum(group => group.Count);

    public bool HasNoResults => VisibleItemCount == 0;

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

    public void ApplyFilter()
    {
        foreach (var group in Groups)
        {
            group.ApplyFilter(SearchText);
        }

        OnPropertyChanged(nameof(VisibleItemCount));
        OnPropertyChanged(nameof(HasNoResults));
    }
}

public sealed class PreviewCanvasViewModel : ViewModelBase
{
    private readonly SceneViewportState _viewport = new();
    private bool _isGridVisible = true;
    private bool _isSafeFrameVisible = true;
    private LayerItemViewModel? _selectedLayer;
    private string _sceneName = "Cena principal";
    private string _sceneRole = "Cena principal";
    private string _canvasSize = "1920×1080";
    private string _frameRate = "60 fps";
    private bool _isFitZoom = true;

    public PreviewCanvasViewModel()
    {
        ToggleGridCommand = new RelayCommand(() => IsGridVisible = !IsGridVisible);
        ToggleSafeFrameCommand = new RelayCommand(() => IsSafeFrameVisible = !IsSafeFrameVisible);
        FitZoomCommand = new RelayCommand(FitZoom);
        ActualSizeCommand = new RelayCommand(() =>
        {
            IsFitZoom = false;
            Viewport.SetZoomAt(ViewportCenter, 1);
            NotifyViewportChanged();
        });
        ZoomInCommand = new RelayCommand(() => ZoomAtCenter(1.12));
        ZoomOutCommand = new RelayCommand(() => ZoomAtCenter(1 / 1.12));
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(RequestLayerSelection);
    }

    public event EventHandler<LayerSelectionRequestedEventArgs>? LayerSelectionRequested;

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public SceneViewportState Viewport => _viewport;

    public double CanvasWidth => _viewport.CanvasWidth;

    public double CanvasHeight => _viewport.CanvasHeight;

    public double Zoom => _viewport.Zoom;

    public double PanX => _viewport.OffsetX;

    public double PanY => _viewport.OffsetY;

    public Point ViewportCenter => new(_viewport.ViewportWidth / 2, _viewport.ViewportHeight / 2);

    public string SceneName
    {
        get => _sceneName;
        set => SetProperty(ref _sceneName, value);
    }

    public string SceneRole
    {
        get => _sceneRole;
        set => SetProperty(ref _sceneRole, value);
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

    public string ZoomLabel => IsFitZoom ? "Fit" : Zoom >= 0.995 && Zoom <= 1.005 ? "100%" : $"{Zoom * 100:0}%";

    public ICommand ToggleGridCommand { get; }

    public ICommand ToggleSafeFrameCommand { get; }

    public ICommand FitZoomCommand { get; }

    public ICommand ActualSizeCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public IRelayCommand<LayerItemViewModel> SelectLayerCommand { get; }

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

    public string SelectedLayerName => SelectedLayer?.Name ?? "Nenhuma camada";

    public LayerItemViewModel? SelectedLayer
    {
        get => _selectedLayer;
        private set
        {
            if (SetProperty(ref _selectedLayer, value))
            {
                OnPropertyChanged(nameof(SelectedLayerName));
            }
        }
    }

    public bool IsFitZoom
    {
        get => _isFitZoom;
        private set
        {
            if (SetProperty(ref _isFitZoom, value))
            {
                OnPropertyChanged(nameof(ZoomLabel));
            }
        }
    }

    public void SetCanvas(double width, double height, double frameRate, bool isProgram)
    {
        _viewport.CanvasWidth = width;
        _viewport.CanvasHeight = height;
        CanvasSize = $"{width:0}×{height:0}";
        FrameRate = $"{frameRate:0.##} fps";
        SceneRole = isProgram ? "Cena principal" : "Cena em edição";
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        FitZoom();
    }

    public void SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _viewport.ViewportWidth = width;
        _viewport.ViewportHeight = height;
        if (IsFitZoom)
        {
            FitZoom();
        }
        else
        {
            NotifyViewportChanged();
        }
    }

    public void SelectLayerFromOwner(LayerItemViewModel? layer)
    {
        foreach (var item in Layers)
        {
            item.IsSelected = ReferenceEquals(item, layer);
        }

        SelectedLayer = layer;
    }

    public void RequestLayerSelection(LayerItemViewModel? layer)
    {
        SelectLayerFromOwner(layer);
        LayerSelectionRequested?.Invoke(this, new LayerSelectionRequestedEventArgs(layer));
    }

    public Point ScreenToScene(Point screenPoint)
    {
        return _viewport.ScreenToScene(screenPoint);
    }

    public Point SceneToScreen(Point scenePoint)
    {
        return _viewport.SceneToScreen(scenePoint);
    }

    public LayerItemViewModel? HitTest(Point scenePoint)
    {
        return Layers
            .Where(layer => layer.IsVisible
                && scenePoint.X >= layer.X
                && scenePoint.X <= layer.X + layer.Width
                && scenePoint.Y >= layer.Y
                && scenePoint.Y <= layer.Y + layer.Height)
            .OrderByDescending(layer => layer.Order)
            .FirstOrDefault();
    }

    public void MoveLayer(LayerItemViewModel layer, double deltaX, double deltaY, bool constrainAxis)
    {
        IsFitZoom = false;
        if (constrainAxis)
        {
            if (Math.Abs(deltaX) >= Math.Abs(deltaY))
            {
                deltaY = 0;
            }
            else
            {
                deltaX = 0;
            }
        }

        layer.MoveBy(deltaX, deltaY, CanvasWidth, CanvasHeight);
    }

    public void NudgeSelectedLayer(double deltaX, double deltaY, bool largeStep)
    {
        if (SelectedLayer is null)
        {
            return;
        }

        var factor = largeStep ? 10 : 1;
        MoveLayer(SelectedLayer, deltaX * factor, deltaY * factor, constrainAxis: false);
    }

    public void ResizeLayer(
        LayerItemViewModel layer,
        ResizeHandleKind handle,
        double deltaX,
        double deltaY,
        bool keepAspect,
        bool fromCenter)
    {
        IsFitZoom = false;
        layer.Resize(handle, deltaX, deltaY, CanvasWidth, CanvasHeight, keepAspect, fromCenter);
    }

    public void PanBy(double deltaX, double deltaY)
    {
        IsFitZoom = false;
        _viewport.Pan(new Vector(deltaX, deltaY));
        NotifyViewportChanged();
    }

    public void FitZoom()
    {
        _viewport.Fit(48);
        IsFitZoom = true;
        NotifyViewportChanged();
    }

    public void ZoomAtScreenPoint(Point screenPoint, double factor)
    {
        IsFitZoom = false;
        _viewport.ZoomAt(screenPoint, factor);
        NotifyViewportChanged();
    }

    public void ZoomAtCenter(double factor)
    {
        ZoomAtScreenPoint(ViewportCenter, factor);
    }

    public void SetActualSizeAtCenter()
    {
        IsFitZoom = false;
        _viewport.SetZoomAt(ViewportCenter, 1);
        NotifyViewportChanged();
    }

    private void NotifyViewportChanged()
    {
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(PanX));
        OnPropertyChanged(nameof(PanY));
        OnPropertyChanged(nameof(ZoomLabel));
    }
}

public sealed class LayerSelectionRequestedEventArgs : EventArgs
{
    public LayerSelectionRequestedEventArgs(LayerItemViewModel? layer)
    {
        Layer = layer;
    }

    public LayerItemViewModel? Layer { get; }
}

public sealed class ProductionPanelViewModel : ViewModelBase
{
    public ObservableCollection<ProductionOutputCardViewModel> Outputs { get; } = new();
}

public sealed class ProductionOutputCardViewModel : ViewModelBase
{
    public ProductionOutputCardViewModel(
        string id,
        string name,
        StudioIconKind iconKind,
        string sceneName,
        string statusText,
        string transitionText,
        bool isLive,
        bool isRecording,
        bool isConfigured,
        ICommand? sendSceneCommand,
        ICommand? selectCommand)
    {
        Id = id;
        Name = name;
        IconKind = iconKind;
        SceneName = sceneName;
        StatusText = statusText;
        TransitionText = transitionText;
        IsLive = isLive;
        IsRecording = isRecording;
        IsConfigured = isConfigured;
        SendSceneCommand = sendSceneCommand;
        SelectCommand = selectCommand;
    }

    public string Id { get; }

    public string Name { get; }

    public StudioIconKind IconKind { get; }

    public string SceneName { get; }

    public string StatusText { get; }

    public string TransitionText { get; }

    public bool IsLive { get; }

    public bool IsRecording { get; }

    public bool IsConfigured { get; }

    public bool IsWarning => !IsConfigured;

    public ICommand? SendSceneCommand { get; }

    public ICommand? SelectCommand { get; }
}

public sealed class BottomWorkbenchViewModel : ViewModelBase
{
    private BottomTabViewModel? _selectedTab;
    private LayerItemViewModel? _selectedLayer;
    private bool _suppressLayerSelectionCommand;
    private bool _isLayersSelected;
    private bool _isSceneOutputsSelected;
    private string _effectsContextTitle = "Efeitos aparecem nas propriedades";

    public BottomWorkbenchViewModel()
    {
        SelectTabCommand = new RelayCommand<BottomTabViewModel>(SelectTab, tab => tab is not null);
    }

    public ObservableCollection<BottomTabViewModel> Tabs { get; } = new();

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public ObservableCollection<SceneOutputRouteViewModel> SceneOutputs { get; } = new();

    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();

    public ObservableCollection<OutputMonitorItemViewModel> Outputs { get; } = new();

    public ObservableCollection<DiagnosticLogItemViewModel> Diagnostics { get; } = new();

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new();

    public ObservableCollection<AudioStripViewModel> AudioStrips { get; } = new();

    public string EffectsContextTitle
    {
        get => _effectsContextTitle;
        set => SetProperty(ref _effectsContextTitle, value);
    }

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

    public bool IsLayersSelected
    {
        get => _isLayersSelected;
        private set => SetProperty(ref _isLayersSelected, value);
    }

    public bool IsSceneOutputsSelected
    {
        get => _isSceneOutputsSelected;
        private set => SetProperty(ref _isSceneOutputsSelected, value);
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
        IsSceneOutputsSelected = tab.Kind == StudioBottomTabKind.SceneOutputs;
    }
}

public sealed class SceneOutputRouteViewModel
{
    public SceneOutputRouteViewModel(string outputId, string outputName, string sceneName, string stateText, string transitionText, ICommand? sendSceneCommand)
    {
        OutputId = outputId;
        OutputName = outputName;
        SceneName = sceneName;
        StateText = stateText;
        TransitionText = transitionText;
        SendSceneCommand = sendSceneCommand;
    }

    public string OutputId { get; }

    public string OutputName { get; }

    public string SceneName { get; }

    public string StateText { get; }

    public string TransitionText { get; }

    public ICommand? SendSceneCommand { get; }
}

public sealed class StatusBarViewModel : ViewModelBase
{
    private string _statusText = "Pronto";
    private string _sceneText = "Cena principal";
    private string _outputText = "3 saídas configuradas";
    private string _framesText = "0 quadros descartados";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string SceneText
    {
        get => _sceneText;
        set => SetProperty(ref _sceneText, value);
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string FramesText
    {
        get => _framesText;
        set => SetProperty(ref _framesText, value);
    }
}

public sealed class StudioDialogViewModel : ViewModelBase
{
    private bool _isOpen;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string _primaryText = "Confirmar";
    private string _secondaryText = "Cancelar";
    private string _kind = string.Empty;
    private string _targetOutputId = string.Empty;
    private string _selectedTransitionId = "transition-cut";
    private int _transitionDurationMs = 120;
    private bool _requiresLiveConfirmation;

    public ObservableCollection<StudioDialogOptionViewModel> Options { get; } = new();

    public ObservableCollection<TransitionOptionViewModel> TransitionOptions { get; } = new();

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string PrimaryText
    {
        get => _primaryText;
        set => SetProperty(ref _primaryText, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        set => SetProperty(ref _secondaryText, value);
    }

    public string Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(HasOptions));
                OnPropertyChanged(nameof(IsRoutingDialog));
                OnPropertyChanged(nameof(IsSourceDialog));
            }
        }
    }

    public string TargetOutputId
    {
        get => _targetOutputId;
        set => SetProperty(ref _targetOutputId, value);
    }

    public string SelectedTransitionId
    {
        get => _selectedTransitionId;
        set => SetProperty(ref _selectedTransitionId, value);
    }

    public int TransitionDurationMs
    {
        get => _transitionDurationMs;
        set => SetProperty(ref _transitionDurationMs, Math.Clamp(value, 0, 5000));
    }

    public bool RequiresLiveConfirmation
    {
        get => _requiresLiveConfirmation;
        set => SetProperty(ref _requiresLiveConfirmation, value);
    }

    public bool HasOptions => Options.Count > 0;

    public bool IsRoutingDialog => Kind == "route-output";

    public bool IsSourceDialog => Kind == "source-library";

    public void NotifyOptionsChanged()
    {
        OnPropertyChanged(nameof(HasOptions));
    }
}

public sealed class StudioDialogOptionViewModel
{
    public StudioDialogOptionViewModel(string id, string title, string description, StudioIconKind iconKind, string badge, bool isEnabled, ICommand? selectCommand)
    {
        Id = id;
        Title = title;
        Description = description;
        IconKind = iconKind;
        Badge = badge;
        IsEnabled = isEnabled;
        SelectCommand = selectCommand;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public StudioIconKind IconKind { get; }

    public string Badge { get; }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public bool IsEnabled { get; }

    public ICommand? SelectCommand { get; }
}

public sealed class TransitionOptionViewModel
{
    public TransitionOptionViewModel(string id, string name, int durationMs)
    {
        Id = id;
        Name = name;
        DurationMs = durationMs;
    }

    public string Id { get; }

    public string Name { get; }

    public int DurationMs { get; }
}

public sealed class DiagnosticLogItemViewModel
{
    public DiagnosticLogItemViewModel(string time, string level, string message, string category = "Studio")
    {
        Time = time;
        Level = level;
        Message = message;
        Category = category;
    }

    public string Time { get; }

    public string Level { get; }

    public string Category { get; }

    public string Message { get; }
}

public sealed class PerformanceMetricViewModel
{
    public PerformanceMetricViewModel(string name, string value, string detail)
    {
        Name = name;
        Value = value;
        Detail = detail;
    }

    public string Name { get; }

    public string Value { get; }

    public string Detail { get; }
}

public sealed class OutputMonitorItemViewModel
{
    public OutputMonitorItemViewModel(string id, string name, StudioOutputState state, string sceneName, string destination, string bitrate, string health, string type = "")
    {
        Id = id;
        Name = name;
        State = state;
        SceneName = sceneName;
        Destination = destination;
        Bitrate = bitrate;
        Health = health;
        Type = type;
    }

    public string Id { get; }

    public string Name { get; }

    public string Type { get; }

    public StudioOutputState State { get; }

    public string StateText => new WTK.MediaForge.Studio.Localization.StudioDisplayNameService().GetOutputMonitorStateName(State);

    public string SceneName { get; }

    public string Destination { get; }

    public string Bitrate { get; }

    public string Health { get; }

    public ICommand? SelectCommand { get; set; }
}

public sealed class AudioStripViewModel
{
    public AudioStripViewModel(string name, string peak, bool isMuted)
    {
        Name = name;
        Peak = peak;
        IsMuted = isMuted;
    }

    public string Name { get; }

    public string Peak { get; }

    public bool IsMuted { get; }

    public string MuteText => IsMuted ? "Mutado" : "Ativo";
}

public sealed class BottomTabViewModel : ViewModelBase
{
    private bool _isSelected;

    public BottomTabViewModel(StudioBottomTabKind kind, string title, string badge = "")
    {
        Kind = kind;
        Title = title;
        Badge = badge;
    }

    public StudioBottomTabKind Kind { get; }

    public string Title { get; }

    public string Badge { get; }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public ICommand? SelectCommand { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
