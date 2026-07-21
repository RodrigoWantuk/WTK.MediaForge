using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Localization;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
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
                OnPropertyChanged(nameof(IsStreamingLive));
                OnPropertyChanged(nameof(IsStreamNotConfigured));
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
                OnPropertyChanged(nameof(IsRecordingLive));
                OnPropertyChanged(nameof(IsRecordingNotConfigured));
                OnPropertyChanged(nameof(RecordingButtonClasses));
            }
        }
    }

    public bool IsStreamBusy => StreamingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public bool IsRecordingBusy => RecordingState is StudioOutputUiState.Starting or StudioOutputUiState.Stopping;

    public bool IsStreamingLive => StreamingState == StudioOutputUiState.Running;

    public bool IsRecordingLive => RecordingState == StudioOutputUiState.Running;

    public bool IsStreamNotConfigured => StreamingState == StudioOutputUiState.NotConfigured;

    public bool IsRecordingNotConfigured => RecordingState == StudioOutputUiState.NotConfigured;

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
    private StudioExplorerTabKind _selectedTab = StudioExplorerTabKind.Scenes;

    public ObservableCollection<ProjectTreeGroupViewModel> Groups { get; } = new();

    public ObservableCollection<SceneCardViewModel> Scenes { get; } = new();

    public ObservableCollection<SourceCardViewModel> Sources { get; } = new();

    public ObservableCollection<OutputCardViewModel> Outputs { get; } = new();

    public ProjectExplorerViewModel()
    {
        SelectTabCommand = new RelayCommand<StudioExplorerTabKind>(SelectTab);
    }

    public ICommand? AddSceneCommand { get; set; }

    public ICommand? AddSourceCommand { get; set; }

    public ICommand? AddOutputCommand { get; set; }

    public IRelayCommand<StudioExplorerTabKind> SelectTabCommand { get; }

    public StudioExplorerTabKind SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsScenesTabSelected));
                OnPropertyChanged(nameof(IsSourcesTabSelected));
                OnPropertyChanged(nameof(IsOutputsTabSelected));
                OnPropertyChanged(nameof(TabTitle));
                OnPropertyChanged(nameof(SearchPlaceholder));
                OnPropertyChanged(nameof(AddButtonTip));
                OnPropertyChanged(nameof(AddButtonText));
                OnPropertyChanged(nameof(CurrentVisibleItemCount));
                OnPropertyChanged(nameof(CurrentTabHasNoResults));
            }
        }
    }

    public bool IsScenesTabSelected => SelectedTab == StudioExplorerTabKind.Scenes;

    public bool IsSourcesTabSelected => SelectedTab == StudioExplorerTabKind.Sources;

    public bool IsOutputsTabSelected => SelectedTab == StudioExplorerTabKind.Outputs;

    public string TabTitle => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => "Entradas",
        StudioExplorerTabKind.Outputs => "Saídas",
        _ => "Cenas"
    };

    public string SearchPlaceholder => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => "Buscar entrada...",
        StudioExplorerTabKind.Outputs => "Buscar saída...",
        _ => "Buscar cena..."
    };

    public string AddButtonTip => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => "Adicionar entrada",
        StudioExplorerTabKind.Outputs => "Adicionar saída",
        _ => "Adicionar cena"
    };

    public string AddButtonText => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => "Entrada",
        StudioExplorerTabKind.Outputs => "Saída",
        _ => "Cena"
    };

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

    public int CurrentVisibleItemCount => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => Sources.Count(item => item.IsVisible),
        StudioExplorerTabKind.Outputs => Outputs.Count(item => item.IsVisible),
        _ => Scenes.Count(item => item.IsVisible)
    };

    public bool HasNoResults => VisibleItemCount == 0;

    public bool CurrentTabHasNoResults => CurrentVisibleItemCount == 0;

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

        ApplyCardFilter(Scenes);
        ApplyCardFilter(Sources);
        ApplyCardFilter(Outputs);
        OnPropertyChanged(nameof(VisibleItemCount));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(CurrentVisibleItemCount));
        OnPropertyChanged(nameof(CurrentTabHasNoResults));
    }

    public ICommand? CurrentAddCommand => SelectedTab switch
    {
        StudioExplorerTabKind.Sources => AddSourceCommand,
        StudioExplorerTabKind.Outputs => AddOutputCommand,
        _ => AddSceneCommand
    };

    public void SelectTab(StudioExplorerTabKind tab)
    {
        SelectedTab = tab;
        OnPropertyChanged(nameof(CurrentAddCommand));
        ApplyFilter();
    }

    private void ApplyCardFilter<T>(IEnumerable<T> cards)
        where T : ProjectCardViewModel
    {
        foreach (var card in cards)
        {
            card.IsVisible = card.Matches(SearchText);
        }
    }
}

public enum SafeAreaDisplayMode
{
    Hidden,
    Visible
}

public sealed class PreviewCanvasViewModel : ViewModelBase
{
    private readonly StudioSceneEditorState _editorState = new();
    private bool _isGridVisible = true;
    private bool _isSafeFrameVisible = true;
    private LayerItemViewModel? _selectedLayer;
    private string _sceneName = "Cena principal";
    private string _sceneRole = "Cena principal";
    private string _canvasSize = "1920×1080";
    private string _frameRate = "60 fps";
    private bool _isFitZoom = true;
    private bool _hasPendingChanges;
    private SafeAreaDisplayMode _safeAreaMode = SafeAreaDisplayMode.Visible;
    private double _safeAreaMarginPercent = 5;
    private string _safeAreaProfileLabel = string.Empty;

    public PreviewCanvasViewModel()
    {
        ToggleGridCommand = new RelayCommand(() => IsGridVisible = !IsGridVisible);
        ToggleSafeFrameCommand = new RelayCommand(() => SafeAreaMode = SafeAreaMode == SafeAreaDisplayMode.Hidden ? SafeAreaDisplayMode.Visible : SafeAreaDisplayMode.Hidden);
        SetSafeAreaModeCommand = new RelayCommand<string>(SetSafeAreaMode);
        FitZoomCommand = new RelayCommand(FitZoom);
        ActualSizeCommand = new RelayCommand(() =>
        {
            IsFitZoom = false;
            Transform.SetZoomAt(ViewportCenter, 1);
            NotifyViewportChanged();
        });
        ZoomInCommand = new RelayCommand(() => ZoomAtCenter(1.12));
        ZoomOutCommand = new RelayCommand(() => ZoomAtCenter(1 / 1.12));
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(RequestLayerSelection);
    }

    public event EventHandler<LayerSelectionRequestedEventArgs>? LayerSelectionRequested;

    public event EventHandler? SceneEdited;

    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    public StudioSceneEditorState EditorState => _editorState;

    public SceneEditorTransform Transform => _editorState.Transform;

    public SceneEditorSnapSettings Snap => _editorState.Snap;

    public double CanvasWidth => Transform.CanvasWidth;

    public double CanvasHeight => Transform.CanvasHeight;

    public double Zoom => Transform.Zoom;

    public double PanX => Transform.PanX;

    public double PanY => Transform.PanY;

    public Point ViewportCenter => new(Transform.ViewportWidth / 2, Transform.ViewportHeight / 2);

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

    public string ZoomLabel => IsFitZoom
        ? LocalizationManager.Instance["Preview_Fit"]
        : Zoom >= 0.995 && Zoom <= 1.005
            ? LocalizationManager.Instance["Preview_ZoomActualSize"]
            : $"{Zoom * 100:0}%";

    public ICommand ToggleGridCommand { get; }

    public ICommand ToggleSafeFrameCommand { get; }

    public IRelayCommand<string> SetSafeAreaModeCommand { get; }

    public ICommand FitZoomCommand { get; }

    public ICommand ActualSizeCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public IRelayCommand<LayerItemViewModel> SelectLayerCommand { get; }

    public ICommand? AddSourceCommand { get; set; }

    public ICommand? ApplySceneDraftCommand { get; set; }

    public ICommand? DiscardSceneDraftCommand { get; set; }

    public Func<StudioShortcutGesture, bool>? ShortcutHandler { get; set; }

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

    public SafeAreaDisplayMode SafeAreaMode
    {
        get => _safeAreaMode;
        set
        {
            if (SetProperty(ref _safeAreaMode, value))
            {
                IsSafeFrameVisible = value != SafeAreaDisplayMode.Hidden;
                OnPropertyChanged(nameof(SafeAreaModeLabel));
            }
        }
    }

    public double SafeAreaMarginPercent
    {
        get => _safeAreaMarginPercent;
        private set
        {
            if (SetProperty(ref _safeAreaMarginPercent, value))
            {
                OnPropertyChanged(nameof(SafeAreaModeLabel));
            }
        }
    }

    public string SafeAreaProfileLabel
    {
        get => _safeAreaProfileLabel;
        private set => SetProperty(ref _safeAreaProfileLabel, value);
    }

    public string SafeAreaModeLabel => SafeAreaMode switch
    {
        SafeAreaDisplayMode.Hidden => LocalizationManager.Instance["Preview_SafeArea_Hidden"],
        _ => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance["Preview_SafeArea_Visible"],
            SafeAreaMarginPercent)
    };

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

    public bool HasPendingChanges
    {
        get => _hasPendingChanges;
        set
        {
            if (SetProperty(ref _hasPendingChanges, value))
            {
                OnPropertyChanged(nameof(DraftStateText));
            }
        }
    }

    public string DraftStateText => HasPendingChanges ? "Alterações não aplicadas" : "Cena aplicada";

    public void SetCanvas(double width, double height, double frameRate, bool isProgram)
    {
        Transform.CanvasWidth = width;
        Transform.CanvasHeight = height;
        CanvasSize = $"{width:0}×{height:0}";
        FrameRate = $"{frameRate:0.##} fps";
        SceneRole = isProgram ? "Cena principal" : "Cena em edição";
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        FitZoom();
    }

    public void SetSafeAreaProfile(double marginPercent, string outputLabel)
    {
        SafeAreaMarginPercent = marginPercent;
        SafeAreaProfileLabel = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance["Preview_SafeArea_OutputProfile"],
            outputLabel);
    }

    public void SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Transform.ViewportWidth = width;
        Transform.ViewportHeight = height;
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
        return Transform.ViewportToScene(screenPoint);
    }

    public Point SceneToScreen(Point scenePoint)
    {
        return Transform.SceneToViewport(scenePoint);
    }

    public LayerItemViewModel? HitTest(Point scenePoint)
    {
        return SceneEditorHitTest.HitTestLayer(Layers, scenePoint);
    }

    public bool ExecuteShortcut(StudioShortcutGesture gesture)
    {
        return ShortcutHandler?.Invoke(gesture) == true;
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
        MarkSceneEdited();
    }

    public void MoveLayerFromStart(
        LayerItemViewModel layer,
        double startX,
        double startY,
        Vector sceneDelta,
        KeyModifiers modifiers)
    {
        if (layer.IsLocked)
        {
            return;
        }

        IsFitZoom = false;
        var deltaX = sceneDelta.X;
        var deltaY = sceneDelta.Y;
        if (modifiers.HasFlag(KeyModifiers.Shift))
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

        var targetX = Math.Clamp(startX + deltaX, 0, Math.Max(0, CanvasWidth - layer.Width));
        var targetY = Math.Clamp(startY + deltaY, 0, Math.Max(0, CanvasHeight - layer.Height));
        var snap = Snap.GetMoveSnap(modifiers);
        targetX = SceneEditorSnapSettings.Snap(targetX, snap);
        targetY = SceneEditorSnapSettings.Snap(targetY, snap);
        layer.X = Math.Round(Math.Clamp(targetX, 0, Math.Max(0, CanvasWidth - layer.Width)));
        layer.Y = Math.Round(Math.Clamp(targetY, 0, Math.Max(0, CanvasHeight - layer.Height)));
        MarkSceneEdited();
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

    public void NudgeSelectedLayer(double deltaX, double deltaY, KeyModifiers modifiers)
    {
        if (SelectedLayer is null || SelectedLayer.IsLocked)
        {
            return;
        }

        var factor = Snap.GetNudgeSize(modifiers);
        MoveLayer(SelectedLayer, deltaX * factor, deltaY * factor, constrainAxis: false);
    }

    public void ToggleLayerVisibility(LayerItemViewModel layer)
    {
        layer.IsVisible = !layer.IsVisible;
        MarkSceneEdited();
    }

    public void ToggleLayerLock(LayerItemViewModel layer)
    {
        layer.IsLocked = !layer.IsLocked;
        MarkSceneEdited();
    }

    public void BringLayerToFront(LayerItemViewModel layer)
    {
        layer.Order = Layers.Count == 0 ? 1 : Layers.Max(item => item.Order) + 1;
        NormalizeLayerOrder();
        MarkSceneEdited();
    }

    public void SendLayerToBack(LayerItemViewModel layer)
    {
        layer.Order = Layers.Count == 0 ? 1 : Layers.Min(item => item.Order) - 1;
        NormalizeLayerOrder();
        MarkSceneEdited();
    }

    public void ResetLayerTransform(LayerItemViewModel layer)
    {
        layer.X = Math.Round(CanvasWidth * 0.08);
        layer.Y = Math.Round(CanvasHeight * 0.08);
        layer.Width = Math.Round(CanvasWidth * 0.4);
        layer.Height = Math.Round(CanvasHeight * 0.4);
        layer.RotationDegrees = 0;
        layer.Opacity = 100;
        MarkSceneEdited();
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
        MarkSceneEdited();
    }

    public void ResizeLayerFromStart(
        LayerItemViewModel layer,
        ResizeHandleKind handle,
        Rect startBounds,
        Vector sceneDelta,
        KeyModifiers modifiers)
    {
        if (layer.IsLocked || handle == ResizeHandleKind.None)
        {
            return;
        }

        IsFitZoom = false;
        var result = SceneEditorResizeGeometry.Resize(
            startBounds,
            handle,
            sceneDelta,
            layer.RotationDegrees,
            new Point(layer.PivotX, layer.PivotY),
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Alt),
            Snap.GetResizeSnap(modifiers));
        layer.X = Math.Round(result.X);
        layer.Y = Math.Round(result.Y);
        layer.Width = Math.Min(CanvasWidth, Math.Round(result.Width));
        layer.Height = Math.Min(CanvasHeight, Math.Round(result.Height));
        MarkSceneEdited();
    }

    public void PanBy(double deltaX, double deltaY)
    {
        IsFitZoom = false;
        Transform.PanBy(new Vector(deltaX, deltaY));
        NotifyViewportChanged();
    }

    public void FitZoom()
    {
        Transform.Fit(48);
        IsFitZoom = true;
        NotifyViewportChanged();
    }

    public void ZoomAtScreenPoint(Point screenPoint, double factor)
    {
        IsFitZoom = false;
        Transform.ZoomAt(screenPoint, factor);
        NotifyViewportChanged();
    }

    public void ZoomAtCenter(double factor)
    {
        ZoomAtScreenPoint(ViewportCenter, factor);
    }

    public void SetActualSizeAtCenter()
    {
        IsFitZoom = false;
        Transform.SetZoomAt(ViewportCenter, 1);
        NotifyViewportChanged();
    }

    private void NotifyViewportChanged()
    {
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(PanX));
        OnPropertyChanged(nameof(PanY));
        OnPropertyChanged(nameof(ZoomLabel));
    }

    private void NormalizeLayerOrder()
    {
        var ordered = Layers.OrderBy(item => item.Order).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i].Order = i + 1;
        }
    }

    private void MarkSceneEdited()
    {
        HasPendingChanges = true;
        SceneEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SetSafeAreaMode(string? mode)
    {
        SafeAreaMode = mode switch
        {
            "hidden" => SafeAreaDisplayMode.Hidden,
            "visible" => SafeAreaDisplayMode.Visible,
            _ => SafeAreaMode
        };
    }
}

public sealed class PreviewWorkspaceViewModel : ViewModelBase
{
    public PreviewWorkspaceViewModel(PreviewCanvasViewModel preview)
    {
        Preview = preview;
    }

    public PreviewCanvasViewModel Preview { get; }
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
        string routeButtonText,
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
        RouteButtonText = routeButtonText;
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

    public string RouteButtonText { get; }

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
    private string _centerText = "Cena: Cena principal | Saídas: 3/4 configuradas | Prévia: 60 fps | 0 quadros descartados";
    private string _liveText = string.Empty;
    private string _recordingText = string.Empty;

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

    public string CenterText
    {
        get => _centerText;
        set => SetProperty(ref _centerText, value);
    }

    public string LiveText
    {
        get => _liveText;
        set
        {
            if (SetProperty(ref _liveText, value))
            {
                OnPropertyChanged(nameof(HasRightStatus));
                OnPropertyChanged(nameof(RightText));
            }
        }
    }

    public string RecordingText
    {
        get => _recordingText;
        set
        {
            if (SetProperty(ref _recordingText, value))
            {
                OnPropertyChanged(nameof(HasRightStatus));
                OnPropertyChanged(nameof(RightText));
            }
        }
    }

    public bool HasRightStatus => !string.IsNullOrWhiteSpace(RightText);

    public string RightText => string.Join("  ", new[] { LiveText, RecordingText }.Where(item => !string.IsNullOrWhiteSpace(item)));
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
    private string _selectedSceneId = string.Empty;
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
                OnPropertyChanged(nameof(IsSettingsDialog));
                OnPropertyChanged(nameof(IsPrimaryActionEnabled));
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

    public string SelectedSceneId
    {
        get => _selectedSceneId;
        set
        {
            if (SetProperty(ref _selectedSceneId, value))
            {
                OnPropertyChanged(nameof(HasSelectedScene));
                OnPropertyChanged(nameof(IsPrimaryActionEnabled));
            }
        }
    }

    public bool HasSelectedScene => !string.IsNullOrWhiteSpace(SelectedSceneId);

    public bool IsPrimaryActionEnabled => !IsRoutingDialog || HasSelectedScene;

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

    public bool IsSettingsDialog => Kind == "settings";

    public void NotifyOptionsChanged()
    {
        OnPropertyChanged(nameof(HasOptions));
    }
}

public sealed class StudioDialogOptionViewModel
    : ViewModelBase
{
    private bool _isSelected;

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

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
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
