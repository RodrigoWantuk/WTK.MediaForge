using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class TitleBarViewModel : ViewModelBase
{
    private string _projectName = "Live Production Workspace";
    private string _workspaceState = "Modo edicao";

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
    private string _stateBadge = "Modo edicao";
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
        StudioOutputUiState.Running => "primary live",
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
    private bool _isGridVisible = true;
    private bool _isSafeFrameVisible = true;
    private LayerItemViewModel? _selectedLayer;
    private string _sceneName = "Main Scene";
    private string _sceneRole = "Cena em edicao";
    private string _canvasSize = "1920 x 1080";
    private string _frameRate = "60 fps";
    private double _zoom = 0.82;
    private double _panX;
    private double _panY;
    private double _viewportWidth;
    private double _viewportHeight;
    private bool _isFitZoom = true;

    public PreviewCanvasViewModel()
    {
        ToggleGridCommand = new RelayCommand(() => IsGridVisible = !IsGridVisible);
        ToggleSafeFrameCommand = new RelayCommand(() => IsSafeFrameVisible = !IsSafeFrameVisible);
        FitZoomCommand = new RelayCommand(FitZoom);
        ActualSizeCommand = new RelayCommand(() =>
        {
            IsFitZoom = false;
            Zoom = 1;
            CenterCanvas();
        });
        ZoomInCommand = new RelayCommand(() => ZoomAtCenter(0.1));
        ZoomOutCommand = new RelayCommand(() => ZoomAtCenter(-0.1));
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(RequestLayerSelection, layer => layer is not null);
    }

    public event EventHandler<LayerSelectionRequestedEventArgs>? LayerSelectionRequested;

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public double CanvasWidth { get; private set; } = 1920;

    public double CanvasHeight { get; private set; } = 1080;

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

    public string ZoomLabel => IsFitZoom ? "Ajustar" : Zoom >= 0.995 && Zoom <= 1.005 ? "100%" : $"{Zoom * 100:0}%";

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

    public string SelectedLayerName => SelectedLayer?.Name ?? "Nenhuma camada selecionada";

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

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Clamp(value, 0.1, 4)))
            {
                if (!_isFitZoom)
                {
                    OnPropertyChanged(nameof(ZoomLabel));
                }
            }
        }
    }

    public double PanX
    {
        get => _panX;
        set => SetProperty(ref _panX, value);
    }

    public double PanY
    {
        get => _panY;
        set => SetProperty(ref _panY, value);
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
        CanvasWidth = width;
        CanvasHeight = height;
        CanvasSize = $"{width:0} x {height:0}";
        FrameRate = $"{frameRate:0.##} fps";
        SceneRole = isProgram ? "Cena principal" : "Cena em edicao";
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

        _viewportWidth = width;
        _viewportHeight = height;
        if (IsFitZoom)
        {
            FitZoom();
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
        if (layer is null)
        {
            return;
        }

        SelectLayerFromOwner(layer);
        LayerSelectionRequested?.Invoke(this, new LayerSelectionRequestedEventArgs(layer));
    }

    public LayerItemViewModel? HitTest(double canvasX, double canvasY)
    {
        return Layers
            .Where(layer => layer.IsVisible
                && canvasX >= layer.X
                && canvasX <= layer.X + layer.Width
                && canvasY >= layer.Y
                && canvasY <= layer.Y + layer.Height)
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
        PanX += deltaX;
        PanY += deltaY;
    }

    public void FitZoom()
    {
        var availableWidth = _viewportWidth > 0 ? Math.Max(320, _viewportWidth - 72) : 1600;
        var availableHeight = _viewportHeight > 0 ? Math.Max(220, _viewportHeight - 72) : 900;
        var zoomX = availableWidth / CanvasWidth;
        var zoomY = availableHeight / CanvasHeight;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.1, 4);
        IsFitZoom = true;
        CenterCanvas();
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomLabel));
    }

    public void ZoomAtCenter(double delta)
    {
        IsFitZoom = false;
        Zoom = Math.Clamp(Zoom + delta, 0.1, 4);
        CenterCanvas();
    }

    private void CenterCanvas()
    {
        if (_viewportWidth <= 0 || _viewportHeight <= 0)
        {
            PanX = 0;
            PanY = 0;
            return;
        }

        PanX = (_viewportWidth - CanvasWidth * Zoom) / 2;
        PanY = (_viewportHeight - CanvasHeight * Zoom) / 2;
    }
}

public sealed class LayerSelectionRequestedEventArgs : EventArgs
{
    public LayerSelectionRequestedEventArgs(LayerItemViewModel layer)
    {
        Layer = layer;
    }

    public LayerItemViewModel Layer { get; }
}

public sealed class BottomWorkbenchViewModel : ViewModelBase
{
    private BottomTabViewModel? _selectedTab;
    private LayerItemViewModel? _selectedLayer;
    private bool _suppressLayerSelectionCommand;
    private bool _isLayersSelected;
    private bool _isEffectsSelected;
    private bool _isOutputsSelected;
    private string _effectsContextTitle = "Selecione uma camada";

    public BottomWorkbenchViewModel()
    {
        SelectTabCommand = new RelayCommand<BottomTabViewModel>(SelectTab, tab => tab is not null);
    }

    public ObservableCollection<BottomTabViewModel> Tabs { get; } = new();

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();

    public ObservableCollection<OutputMonitorItemViewModel> Outputs { get; } = new();

    public ObservableCollection<DiagnosticLogItemViewModel> Diagnostics { get; } = new();

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new();

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

    public string EffectsContextTitle
    {
        get => _effectsContextTitle;
        set => SetProperty(ref _effectsContextTitle, value);
    }

    public bool HasEffectsContext => Effects.Count > 0;

    public void NotifyEffectsChanged()
    {
        OnPropertyChanged(nameof(HasEffectsContext));
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

    public bool IsOutputsSelected
    {
        get => _isOutputsSelected;
        private set => SetProperty(ref _isOutputsSelected, value);
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
        IsOutputsSelected = tab.Kind == StudioBottomTabKind.Outputs;
    }
}

public sealed class StatusBarViewModel : ViewModelBase
{
    private string _statusText = "Pronto";
    private string _sceneText = "Cena Main Scene";
    private string _outputText = "3 saidas configuradas";
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
        set => SetProperty(ref _kind, value);
    }
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

public sealed class OutputMonitorItemViewModel : ViewModelBase
{
    private readonly StudioOutputState _state;

    public OutputMonitorItemViewModel(string id, string name, StudioOutputState state, string sceneName, string destination, string bitrate, string health, string type = "")
    {
        Id = id;
        Name = name;
        _state = state;
        SceneName = sceneName;
        Destination = destination;
        Bitrate = bitrate;
        Health = health;
        Type = type;
    }

    public string Id { get; }

    public string Name { get; }

    public string Type { get; }

    public StudioOutputState State => _state;

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
