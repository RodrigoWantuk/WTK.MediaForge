using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Docking;
using WTK.MediaForge.Studio.Localization;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels.Docking;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class StudioShellViewModel : ViewModelBase
{
    private readonly IStudioProjectService _projectService;
    private readonly IStudioOutputService _outputService;
    private readonly IStudioDiagnosticsService _diagnosticsService;
    private readonly IStudioSelectionService _selectionService;
    private readonly IStudioUiTimer _uiTimer;
    private readonly StudioLayoutService _layoutService = new();
    private readonly SceneEditSessionService _sceneEditSessionService = new();
    private StudioLayoutDocument _layoutDocument = new();
    private StudioDocument _document = StudioMockDocumentFactory.Create();
    private ProjectTreeItemViewModel? _selectedProjectItem;
    private LayerItemViewModel? _selectedLayer;
    private StudioScene? _currentScene;
    private StudioSource? _selectedSource;
    private StudioOutput? _selectedOutput;
    private StudioSelectionState _currentSelection = StudioSelectionState.None;
    private IDock? _dockLayout;
    private SceneEditSession? _editSession;

    public StudioShellViewModel()
        : this(StudioServiceFactory.CreateFake())
    {
    }

    public StudioShellViewModel(StudioServiceBundle services)
        : this(
            services.ProjectService,
            services.OutputService,
            services.DiagnosticsService,
            services.SelectionService,
            services.UiTimer)
    {
    }

    public StudioShellViewModel(
        IStudioProjectService projectService,
        IStudioOutputService outputService,
        IStudioDiagnosticsService diagnosticsService,
        IStudioSelectionService selectionService,
        IStudioUiTimer uiTimer)
    {
        _projectService = projectService;
        _outputService = outputService;
        _diagnosticsService = diagnosticsService;
        _selectionService = selectionService;
        _uiTimer = uiTimer;

        BottomWorkbench = new BottomWorkbenchViewModel();
        PreviewWorkspace = new PreviewWorkspaceViewModel(Preview);
        DockFactory = new StudioDockFactory(this);
        DockLayout = DockFactory.CreateLayout();
        DockFactory.InitLayout(DockLayout);
        NavigationDock = new StudioDockPanelViewModel("navigation", "Navegação", ProjectExplorer);
        ProductionDock = new StudioDockPanelViewModel("production", "Produção", Production);
        PropertiesDock = new StudioDockPanelViewModel("properties", "Propriedades", Inspector);
        WorkbenchDock = new StudioDockPanelViewModel("workbench", "Camadas e saídas", BottomWorkbench);
        Preview.LayerSelectionRequested += OnPreviewLayerSelectionRequested;

        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        AddSourceCommand = new RelayCommand(OpenAddSourceDialog);
        AddSceneCommand = new RelayCommand(OpenAddSceneDialog);
        ConfigureOutputCommand = new RelayCommand(OpenConfigureOutputDialog);
        SettingsCommand = new RelayCommand(OpenSettingsDialog);
        RestoreLayoutCommand = new RelayCommand(RestoreDefaultLayout);
        RedockAllPanelsCommand = new RelayCommand(RedockAllPanels);
        ApplySceneDraftCommand = new RelayCommand(ApplySceneDraft, () => _editSession?.HasChanges == true);
        DiscardSceneDraftCommand = new RelayCommand(DiscardSceneDraft, () => _editSession?.HasChanges == true);
        ToggleStreamingCommand = new AsyncRelayCommand(ToggleStreamingAsync, CanToggleStreaming);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        SelectProjectItemCommand = new RelayCommand<ProjectTreeItemViewModel>(SelectProjectItem, item => item is not null);
        SelectLayerCommand = new RelayCommand<LayerItemViewModel>(SelectLayer, layer => layer is not null);
        ToggleLayerVisibilityCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerVisibility, layer => layer is not null);
        ToggleLayerLockCommand = new RelayCommand<LayerItemViewModel>(ToggleLayerLock, layer => layer is not null);
        MoveLayerUpCommand = new RelayCommand<LayerItemViewModel>(MoveLayerUp, layer => layer is not null);
        MoveLayerDownCommand = new RelayCommand<LayerItemViewModel>(MoveLayerDown, layer => layer is not null);
        ToggleEffectEnabledCommand = new RelayCommand<EffectItemViewModel>(ToggleEffectEnabled, effect => effect is not null);
        AddSelectedSourceToCurrentSceneCommand = new RelayCommand(AddSelectedSourceToCurrentScene, () => _selectedSource is not null && CurrentScene is not null);
        ReconnectSourceCommand = new RelayCommand(() => SetStatus("Reconexão de fonte agendada."));
        ConfirmDialogCommand = new RelayCommand(ConfirmDialog);
        CancelDialogCommand = new RelayCommand(CloseDialog);
        ProjectExplorer.AddSceneCommand = AddSceneCommand;
        ProjectExplorer.AddSourceCommand = AddSourceCommand;
        ProjectExplorer.AddOutputCommand = ConfigureOutputCommand;
        Preview.AddSourceCommand = AddSourceCommand;
        Preview.ApplySceneDraftCommand = ApplySceneDraftCommand;
        Preview.DiscardSceneDraftCommand = DiscardSceneDraftCommand;
        Preview.SceneEdited += OnPreviewSceneEdited;

        _outputService.StatusChanged += OnOutputStatusChanged;
        _selectionService.SelectionChanged += OnSelectionChanged;
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        _layoutDocument = _layoutService.Load();
        ApplyLayoutDocument(_layoutDocument);
        LoadDesignData(_document, _diagnosticsService.Items);
        ApplyProjectDocument();
        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
    }

    public event EventHandler? SettingsRequested;

    public TitleBarViewModel TitleBar { get; } = new();

    public ToolbarViewModel Toolbar { get; } = new();

    public ProjectExplorerViewModel ProjectExplorer { get; } = new();

    public PreviewCanvasViewModel Preview { get; } = new();

    public PreviewWorkspaceViewModel PreviewWorkspace { get; }

    public ProductionPanelViewModel Production { get; } = new();

    public InspectorHostViewModel Inspector { get; } = new();

    public BottomWorkbenchViewModel BottomWorkbench { get; }

    public StatusBarViewModel StatusBar { get; } = new();

    public StudioDialogViewModel Dialog { get; } = new();

    public StudioDockFactory DockFactory { get; }

    public IDock? DockLayout
    {
        get => _dockLayout;
        private set => SetProperty(ref _dockLayout, value);
    }

    public StudioDockPanelViewModel NavigationDock { get; }

    public StudioDockPanelViewModel ProductionDock { get; }

    public StudioDockPanelViewModel PropertiesDock { get; }

    public StudioDockPanelViewModel WorkbenchDock { get; }

    public IAsyncRelayCommand NewProjectCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IAsyncRelayCommand SaveProjectCommand { get; }

    public ICommand AddSourceCommand { get; }

    public ICommand AddSceneCommand { get; }

    public ICommand ConfigureOutputCommand { get; }

    public ICommand SettingsCommand { get; }

    public ICommand RestoreLayoutCommand { get; }

    public ICommand RedockAllPanelsCommand { get; }

    public IRelayCommand ApplySceneDraftCommand { get; }

    public IRelayCommand DiscardSceneDraftCommand { get; }

    public IAsyncRelayCommand ToggleStreamingCommand { get; }

    public IAsyncRelayCommand ToggleRecordingCommand { get; }

    public IRelayCommand<ProjectTreeItemViewModel> SelectProjectItemCommand { get; }

    public IRelayCommand<LayerItemViewModel> SelectLayerCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerVisibilityCommand { get; }

    public IRelayCommand<LayerItemViewModel> ToggleLayerLockCommand { get; }

    public IRelayCommand<LayerItemViewModel> MoveLayerUpCommand { get; }

    public IRelayCommand<LayerItemViewModel> MoveLayerDownCommand { get; }

    public IRelayCommand<EffectItemViewModel> ToggleEffectEnabledCommand { get; }

    public IRelayCommand AddSelectedSourceToCurrentSceneCommand { get; }

    public ICommand ReconnectSourceCommand { get; }

    public ICommand ConfirmDialogCommand { get; }

    public ICommand CancelDialogCommand { get; }

    public bool IsStreaming => _outputService.StreamingState == StudioOutputUiState.Running;

    public bool IsRecording => _outputService.RecordingState == StudioOutputUiState.Running;

    public StudioDocument Document => _document;

    public void SetDockToolVisible(string toolId, bool isVisible)
    {
        var tool = EnumerateDockables(DockLayout).OfType<Tool>().FirstOrDefault(item => item.Id == toolId);
        if (tool is null)
        {
            return;
        }

        tool.IsOpen = isVisible;
        tool.DockingState = isVisible ? DockingWindowState.Docked : DockingWindowState.Hidden;
        OnPropertyChanged(nameof(DockLayout));
        SetStatus(isVisible ? $"{tool.Title} visível." : $"{tool.Title} oculto.");
    }

    public StudioScene? CurrentScene
    {
        get => _currentScene;
        private set => SetProperty(ref _currentScene, value);
    }

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

    public void LoadDesignData(StudioDocument document, IEnumerable<DiagnosticLogItemViewModel> diagnostics)
    {
        _document = document;
        Replace(_diagnosticsService.Items, diagnostics);
        InitializeBottomTabs();
        EnsureAppliedOutputSnapshots();
        RebuildAll();
        var initialScene = _document.Scenes.FirstOrDefault(scene => scene.Id == _document.SelectedSceneId)
            ?? _document.Scenes.FirstOrDefault();
        if (initialScene is not null)
        {
            SelectScene(initialScene, updateProjectSelection: true);
            ClearLayerSelectionAndShowScene();
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
        ProjectExplorer.SelectFromOwner(item);

        if (item.Kind == StudioProjectItemKind.Scene)
        {
            if (!CanLeaveCurrentScene())
            {
                item.IsSelected = false;
                ProjectExplorer.SelectFromOwner(FindProjectItem(CurrentScene?.Id));
                return;
            }

            var scene = _document.Scenes.First(scene => scene.Id == item.Id);
            SelectScene(scene, updateProjectSelection: false);
            ClearLayerSelectionAndShowScene();
            _selectionService.Select(CreateSelection(item));
        }
    }

    public void SelectSceneCard(SceneCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var scene = _document.Scenes.First(item => item.Id == card.Id);
        if (!CanLeaveCurrentScene())
        {
            RebuildProjectExplorer(CurrentScene?.Id);
            return;
        }

        SelectScene(scene, updateProjectSelection: true);
        ClearLayerSelectionAndShowScene();
        _selectionService.Select(new StudioSelectionState(
            StudioSelectionKind.Scene,
            scene.Id,
            scene.DisplayName,
            "scene.canvas",
            $"{scene.Canvas.Width:0}×{scene.Canvas.Height:0} • {scene.Canvas.FrameRate:0.##} fps",
            SceneOutputsLabel(scene)));
    }

    public void SelectSourceCard(SourceCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var source = _document.Sources.First(item => item.Id == card.Id);
        SelectSource(source);
    }

    public void SelectOutputCard(OutputCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var output = _document.Outputs.First(item => item.Id == card.Id);
        SelectOutput(output);
    }

    public void SelectLayer(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            ClearLayerSelectionAndShowScene();
            return;
        }

        ClearProjectSelection();
        foreach (var item in BottomWorkbench.Layers)
        {
            item.IsSelected = ReferenceEquals(item, layer);
        }

        _selectedSource = null;
        _selectedOutput = null;
        SelectedProjectItem = null;
        SelectedLayer = layer;
        ProjectExplorer.SelectFromOwner(null);
        BottomWorkbench.SelectLayerFromOwner(layer);
        Preview.SelectLayerFromOwner(layer);
        AttachEffectCommands(layer.Effects);
        Inspector.SelectedPage = new LayerInspectorViewModel(layer);
        _selectionService.Select(new StudioSelectionState(
            StudioSelectionKind.Layer,
            layer.Id,
            layer.Name,
            layer.Type,
            layer.Type,
            layer.Source));
    }

    public void AssignOutputToScene(string outputId, string sceneId)
    {
        var output = _document.Outputs.First(item => item.Id == outputId);
        SendSceneToOutput(outputId, sceneId, output.DefaultTransitionId, output.TransitionDurationMs);
    }

    public void SendSceneToOutput(string outputId, string sceneId, string transitionId, int durationMs)
    {
        var output = _document.Outputs.First(item => item.Id == outputId);
        var scene = _document.Scenes.First(item => item.Id == sceneId);
        var wasLive = output.IsLive || output.State == StudioOutputState.Live;
        output.AssignedSceneId = scene.Id;
        output.AppliedSceneSnapshot = SceneEditSessionService.CloneScene(scene);
        output.HasPendingSceneUpdate = false;
        output.DefaultTransitionId = transitionId;
        output.TransitionDurationMs = durationMs;
        output.IsConfigured = true;
        output.IsEnabled = true;
        if (output.State == StudioOutputState.Planned)
        {
            output.State = StudioOutputState.Running;
        }

        _document.HasUnsavedChanges = true;
        RebuildAll();
        SelectOutput(output);
        ApplyProjectDocument();
        SetStatus(wasLive
            ? $"{scene.DisplayName} transicionada em {output.DisplayName}."
            : $"{scene.DisplayName} definida para {output.DisplayName}.");
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void InitializeBottomTabs()
    {
        Replace(
            BottomWorkbench.Tabs,
            new[]
            {
                new BottomTabViewModel(StudioBottomTabKind.Layers, "Camadas"),
                new BottomTabViewModel(StudioBottomTabKind.SceneOutputs, "Saídas da cena")
            });

        foreach (var tab in BottomWorkbench.Tabs)
        {
            tab.SelectCommand = BottomWorkbench.SelectTabCommand;
        }

        BottomWorkbench.SelectTab(BottomWorkbench.Tabs[0]);
    }

    private async Task NewProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.NewAsync(cancellationToken).ConfigureAwait(true);
        _document = StudioMockDocumentFactory.Create();
        _document.DisplayName = "Projeto sem título";
        _document.HasUnsavedChanges = true;
        LoadDesignData(_document, _diagnosticsService.Items);
        ApplyProjectDocument();
        SetStatus("Novo projeto criado.");
    }

    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.OpenAsync("mock-project.mforge.json", cancellationToken).ConfigureAwait(true);
        _document = StudioMockDocumentFactory.Create();
        _document.DisplayName = "Projeto carregado";
        LoadDesignData(_document, _diagnosticsService.Items);
        ApplyProjectDocument();
        SetStatus("Projeto de exemplo aberto.");
    }

    private async Task SaveProjectAsync(CancellationToken cancellationToken)
    {
        await _projectService.SaveAsync(null, cancellationToken).ConfigureAwait(true);
        _document.HasUnsavedChanges = false;
        ApplyProjectDocument();
        SetStatus("Projeto salvo em pacote visual.");
    }

    private bool CanToggleStreaming()
    {
        return GetStreamingOutput() is not null
            && _outputService.StreamingState is not StudioOutputUiState.Starting and not StudioOutputUiState.Stopping;
    }

    private bool CanToggleRecording()
    {
        return GetRecordingOutput() is not null
            && _outputService.RecordingState is not StudioOutputUiState.Starting and not StudioOutputUiState.Stopping;
    }

    private async Task ToggleStreamingAsync(CancellationToken cancellationToken)
    {
        var output = GetStreamingOutput();
        if (output is null)
        {
            OpenConfigureOutputDialog();
            return;
        }

        await _outputService.ToggleStreamingAsync(cancellationToken).ConfigureAwait(true);
        output.IsLive = IsStreaming;
        output.State = IsStreaming ? StudioOutputState.Live : StudioOutputState.Running;
        RebuildAll();
        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
        SetStatus(IsStreaming ? $"Ao vivo com {AssignedSceneName(output)}." : "Transmissão encerrada.");
    }

    private async Task ToggleRecordingAsync(CancellationToken cancellationToken)
    {
        var output = GetRecordingOutput();
        if (output is null)
        {
            OpenConfigureOutputDialog();
            return;
        }

        await _outputService.ToggleRecordingAsync(cancellationToken).ConfigureAwait(true);
        output.IsRecording = IsRecording;
        output.State = IsRecording ? StudioOutputState.Recording : StudioOutputState.Running;
        RebuildAll();
        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
        SetStatus(IsRecording ? $"Gravando {AssignedSceneName(output)}." : "Gravação encerrada.");
    }

    private void ToggleLayerVisibility(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsVisible = !layer.IsVisible;
        SetStatus($"{layer.Name}: {layer.VisibilityGlyph}.");
    }

    private void ToggleLayerLock(LayerItemViewModel? layer)
    {
        if (layer is null)
        {
            return;
        }

        layer.IsLocked = !layer.IsLocked;
        SetStatus($"{layer.Name}: {layer.LockGlyph}.");
    }

    private void MoveLayerUp(LayerItemViewModel? layer)
    {
        MoveLayer(layer, -1);
    }

    private void MoveLayerDown(LayerItemViewModel? layer)
    {
        MoveLayer(layer, 1);
    }

    private void MoveLayer(LayerItemViewModel? layer, int direction)
    {
        if (layer is null)
        {
            return;
        }

        var index = BottomWorkbench.Layers.IndexOf(layer);
        var targetIndex = Math.Clamp(index + direction, 0, BottomWorkbench.Layers.Count - 1);
        if (index == targetIndex)
        {
            return;
        }

        BottomWorkbench.Layers.Move(index, targetIndex);
        Preview.Layers.Move(index, targetIndex);
        RefreshLayerOrder();
        SetStatus($"Ordem de {layer.Name} atualizada.");
    }

    private void ToggleEffectEnabled(EffectItemViewModel? effect)
    {
        if (effect is null)
        {
            return;
        }

        effect.IsEnabled = !effect.IsEnabled;
        SetStatus($"{effect.Name}: {effect.EnabledText}.");
    }

    private void OpenAddSourceDialog()
    {
        Dialog.Options.Clear();
        var options = new[]
        {
            SourceOption("source.webcam", "Webcam", "Capturar câmera local", StudioIconKind.Camera, "Disponível", true),
            SourceOption("source.desktop", "Tela", "Capturar monitor ou janela", StudioIconKind.Desktop, "Disponível", true),
            SourceOption("source.image", "Imagem", "PNG, JPG, WebP", StudioIconKind.Image, "Disponível", true),
            SourceOption("source.media", "Vídeo", "MP4, MOV, WebM", StudioIconKind.Video, "Disponível", true),
            SourceOption("source.text", "Texto", "Título, legenda ou tarja", StudioIconKind.Text, "Disponível", true),
            SourceOption("source.solid", "Cor sólida", "Fundo ou shape simples", StudioIconKind.Source, "Disponível", true),
            SourceOption("source.ndi", "NDI", "Entrada de rede planejada", StudioIconKind.Stream, "Planejado", false),
            SourceOption("source.rtsp", "RTSP/IP", "Câmera IP planejada", StudioIconKind.Camera, "Planejado", false)
        };

        foreach (var option in options)
        {
            Dialog.Options.Add(option);
        }

        Dialog.NotifyOptionsChanged();
        ShowDialog("Adicionar fonte", $"Escolha uma fonte para adicionar à cena {CurrentScene?.DisplayName ?? "atual"}.", "source-library", "Fechar");
    }

    private StudioDialogOptionViewModel SourceOption(string typeId, string title, string description, StudioIconKind icon, string badge, bool isEnabled)
    {
        return new StudioDialogOptionViewModel(
            typeId,
            title,
            description,
            icon,
            badge,
            isEnabled,
            isEnabled ? new RelayCommand(() => AddSourceFromLibrary(typeId, title)) : null);
    }

    private void OpenAddSceneDialog()
    {
        Dialog.Options.Clear();
        Dialog.NotifyOptionsChanged();
        ShowDialog("Adicionar cena", "Cria uma cena vazia pronta para receber fontes.", "scene", "Criar cena");
    }

    private void OpenConfigureOutputDialog()
    {
        var output = _selectedOutput ?? _document.Outputs.FirstOrDefault(item => !item.IsConfigured) ?? _document.Outputs.FirstOrDefault();
        if (output is null)
        {
            ShowDialog("Configurar saídas", "Nenhuma saída disponível no projeto.", "message", "Fechar");
            return;
        }

        SelectOutput(output);
        SetStatus($"Configure {output.DisplayName} no painel de propriedades.");
    }

    private void OpenSettingsDialog()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenSendSceneDialog(string outputId)
    {
        var output = _document.Outputs.First(item => item.Id == outputId);
        var isLive = output.IsLive || output.State == StudioOutputState.Live;
        var defaultTransition = _document.Transitions.FirstOrDefault(item => item.Id == output.DefaultTransitionId);
        Dialog.Options.Clear();
        Dialog.TransitionOptions.Clear();
        foreach (var transition in _document.Transitions)
        {
            Dialog.TransitionOptions.Add(new TransitionOptionViewModel(transition.Id, transition.DisplayName, transition.DurationMs));
        }

        Dialog.TargetOutputId = outputId;
        Dialog.SelectedTransitionId = output.DefaultTransitionId;
        Dialog.TransitionDurationMs = output.TransitionDurationMs > 0
            ? output.TransitionDurationMs
            : defaultTransition?.DurationMs ?? 120;
        Dialog.RequiresLiveConfirmation = isLive;
        Dialog.SelectedSceneId = CurrentScene?.Id ?? output.AssignedSceneId;
        foreach (var scene in _document.Scenes)
        {
            var option = new StudioDialogOptionViewModel(
                scene.Id,
                scene.DisplayName,
                $"{scene.Canvas.Width:0}×{scene.Canvas.Height:0} • {scene.Canvas.FrameRate:0.##} fps",
                StudioIconKind.Scene,
                scene.IsProgram ? "Principal" : string.Empty,
                true,
                new RelayCommand(() => SelectRouteDialogScene(scene.Id)));
            option.IsSelected = option.Id == Dialog.SelectedSceneId;
            Dialog.Options.Add(option);
        }

        Dialog.NotifyOptionsChanged();
        ShowDialog(
            isLive ? "Transicionar cena da saída" : "Alterar cena da saída",
            $"Escolha a cena e a transição para {output.DisplayName}.",
            "route-output",
            isLive ? "Transicionar" : "Alterar");
    }

    private void SelectRouteDialogScene(string sceneId)
    {
        Dialog.SelectedSceneId = sceneId;
        foreach (var option in Dialog.Options)
        {
            option.IsSelected = option.Id == sceneId;
        }
    }

    private void ShowDialog(string title, string message, string kind, string primaryText)
    {
        Dialog.Title = title;
        Dialog.Message = message;
        Dialog.Kind = kind;
        Dialog.PrimaryText = primaryText;
        Dialog.SecondaryText = "Cancelar";
        Dialog.IsOpen = true;
    }

    private void ConfirmDialog()
    {
        switch (Dialog.Kind)
        {
            case "scene":
                AddMockScene();
                CloseDialog();
                break;
            case "settings":
            case "message":
            case "source-library":
                CloseDialog();
                break;
            case "route-output":
                if (Dialog.HasSelectedScene)
                {
                    SendSceneToOutput(
                        Dialog.TargetOutputId,
                        Dialog.SelectedSceneId,
                        Dialog.SelectedTransitionId,
                        Dialog.TransitionDurationMs);
                }

                CloseDialog();
                break;
        }
    }

    private void CloseDialog()
    {
        if (Dialog.Kind == "settings")
        {
            SaveLayoutDocument();
        }

        Dialog.IsOpen = false;
        Dialog.Options.Clear();
        Dialog.TransitionOptions.Clear();
        Dialog.TargetOutputId = string.Empty;
        Dialog.SelectedSceneId = string.Empty;
        Dialog.NotifyOptionsChanged();
    }

    private void RestoreDefaultLayout()
    {
        _layoutDocument = new StudioLayoutDocument();
        DockLayout = DockFactory.CreateLayout();
        DockFactory.InitLayout(DockLayout);
        ApplyLayoutDocument(_layoutDocument);
        SaveLayoutDocument();
        SetStatus("Layout padrão restaurado.");
    }

    private void RedockAllPanels()
    {
        DockLayout = DockFactory.CreateLayout();
        DockFactory.InitLayout(DockLayout);
        foreach (var panel in DockPanels())
        {
            panel.IsFloating = false;
            panel.IsCollapsed = false;
            panel.IsVisible = true;
        }

        SaveLayoutDocument();
        SetStatus("Painéis reencaixados.");
    }

    private void ApplySceneDraft()
    {
        if (_editSession is null || !_editSession.HasChanges)
        {
            return;
        }

        var sceneId = _editSession.SceneId;
        var sceneName = _editSession.Draft.DisplayName;
        _sceneEditSessionService.Apply(_editSession);
        foreach (var output in _document.Outputs.Where(item => item.AssignedSceneId == sceneId))
        {
            output.HasPendingSceneUpdate = true;
        }

        _document.HasUnsavedChanges = true;
        _editSession = _sceneEditSessionService.Create(_editSession.Original);
        CurrentScene = _editSession.Draft;
        Preview.HasPendingChanges = false;
        RebuildAll();
        ClearLayerSelectionAndShowScene();
        ApplyProjectDocument();
        ApplySceneDraftCommand.NotifyCanExecuteChanged();
        DiscardSceneDraftCommand.NotifyCanExecuteChanged();
        SetStatus($"{sceneName} aplicada à cena salva. Saídas vinculadas têm atualização disponível.");
    }

    private void DiscardSceneDraft()
    {
        if (_editSession is null)
        {
            return;
        }

        var sceneName = _editSession.Original.DisplayName;
        _editSession = _sceneEditSessionService.Create(_editSession.Original);
        CurrentScene = _editSession.Draft;
        Preview.HasPendingChanges = false;
        RebuildAll();
        ClearLayerSelectionAndShowScene();
        ApplyProjectDocument();
        ApplySceneDraftCommand.NotifyCanExecuteChanged();
        DiscardSceneDraftCommand.NotifyCanExecuteChanged();
        SetStatus($"Alterações em {sceneName} descartadas.");
    }

    private void OnPreviewSceneEdited(object? sender, EventArgs e)
    {
        MarkSceneDraftChanged();
    }

    private void MarkSceneDraftChanged()
    {
        _editSession?.MarkChanged();
        Preview.HasPendingChanges = _editSession?.HasChanges == true;
        _document.HasUnsavedChanges = true;
        ApplySceneDraftCommand.NotifyCanExecuteChanged();
        DiscardSceneDraftCommand.NotifyCanExecuteChanged();
        ApplyProjectDocument();
    }

    private bool CanLeaveCurrentScene()
    {
        if (_editSession?.HasChanges != true)
        {
            return true;
        }

        SetStatus("Há alterações não aplicadas. Aplique ou descarte antes de trocar de cena.");
        return false;
    }

    private void EnsureAppliedOutputSnapshots()
    {
        foreach (var output in _document.Outputs)
        {
            if (output.AppliedSceneSnapshot is not null)
            {
                continue;
            }

            var scene = _document.Scenes.FirstOrDefault(item => item.Id == output.AssignedSceneId);
            if (scene is not null)
            {
                output.AppliedSceneSnapshot = SceneEditSessionService.CloneScene(scene);
            }
        }
    }

    private void AddSelectedSourceToCurrentScene()
    {
        if (_selectedSource is null)
        {
            return;
        }

        AddSourceLayerToCurrentScene(_selectedSource);
    }

    private void AddSourceFromLibrary(string typeId, string displayName)
    {
        var index = _document.Sources.Count(source => source.TypeId == typeId) + 1;
        var source = new StudioSource
        {
            Id = $"source-{typeId.Replace('.', '-')}-{index}",
            DisplayName = $"{displayName} {index}",
            TypeId = typeId,
            Metadata = SourceMetadata(typeId),
            Endpoint = SourceEndpoint(typeId)
        };
        _document.Sources.Add(source);
        AddSourceLayerToCurrentScene(source);
        CloseDialog();
    }

    private void AddSourceLayerToCurrentScene(StudioSource source)
    {
        if (CurrentScene is null)
        {
            return;
        }

        var index = CurrentScene.Layers.Count + 1;
        var layer = new StudioLayer
        {
            Id = $"layer-{CurrentScene.Id}-{source.Id}-{index}",
            Name = source.DisplayName,
            SourceId = source.Id,
            SourceName = source.DisplayName,
            Type = SourceTypeToLayerType(source.TypeId),
            Order = index
        };
        layer.Transform.X = 160 + index * 24;
        layer.Transform.Y = 140 + index * 18;
        layer.Transform.Width = source.TypeId == "source.text" ? 620 : 480;
        layer.Transform.Height = source.TypeId == "source.text" ? 120 : 270;
        layer.Transform.Opacity = 100;
        layer.Effects.Add(new StudioEffect
        {
            Id = $"{layer.Id}-effect-chroma",
            Name = "Chroma Key",
            Description = "Remove fundo verde com suavidade e controle de spill.",
            IsEnabled = false
        });

        CurrentScene.Layers.Add(layer);
        MarkSceneDraftChanged();
        RebuildSceneLayers();
        var selected = BottomWorkbench.Layers.First(item => item.Id == layer.Id);
        SelectLayer(selected);
        RebuildProjectExplorer();
        RebuildSceneOutputRows();
        ApplyProjectDocument();
        SetStatus($"{source.DisplayName} adicionada a {CurrentScene.DisplayName}.");
    }

    private void AddMockScene()
    {
        if (!CanLeaveCurrentScene())
        {
            return;
        }

        var count = _document.Scenes.Count + 1;
        var scene = new StudioScene
        {
            Id = $"scene-{count}",
            DisplayName = $"Cena {count}",
            Metadata = "1920×1080 • 60 fps"
        };
        scene.Effects.Add(new StudioEffect
        {
            Id = $"{scene.Id}-effect-color",
            Name = "Correção de cor",
            Description = "Planejado para ajustes globais da cena.",
            IsEnabled = false
        });
        _document.Scenes.Add(scene);
        _document.HasUnsavedChanges = true;
        RebuildProjectExplorer(scene.Id);
        SelectScene(scene, updateProjectSelection: true);
        ClearLayerSelectionAndShowScene();
        ApplyProjectDocument();
        SetStatus($"{scene.DisplayName} criada.");
    }

    private void SelectScene(StudioScene scene, bool updateProjectSelection)
    {
        _editSession = _sceneEditSessionService.Create(scene);
        CurrentScene = _editSession.Draft;
        _document.SelectedSceneId = scene.Id;
        Preview.SceneName = CurrentScene.DisplayName;
        Preview.HasPendingChanges = false;
        Preview.SetCanvas(CurrentScene.Canvas.Width, CurrentScene.Canvas.Height, CurrentScene.Canvas.FrameRate, CurrentScene.IsProgram);
        ApplyPreviewSafeAreaForScene(scene);
        RebuildSceneLayers();
        RebuildProjectExplorer(updateProjectSelection ? scene.Id : SelectedProjectItem?.Id);
        RebuildSceneOutputRows();
        StatusBar.SceneText = $"Cena {scene.DisplayName}";
        StatusBar.OutputText = OutputSummary();
        AddSelectedSourceToCurrentSceneCommand.NotifyCanExecuteChanged();
        ApplySceneDraftCommand.NotifyCanExecuteChanged();
        DiscardSceneDraftCommand.NotifyCanExecuteChanged();
    }

    private void SelectOutput(StudioOutput output)
    {
        ClearLayerSelection();
        ClearProjectSelection();
        _selectedSource = null;
        _selectedOutput = output;
        SelectedLayer = null;
        SelectedProjectItem = null;
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
        ApplyPreviewSafeAreaFromOutput(output);

        Inspector.SelectedPage = new OutputInspectorViewModel(
            output,
            AssignedSceneName(output),
            _document.Transitions,
            new RelayCommand(() => OpenSendSceneDialog(output.Id)),
            () => ApplyPreviewSafeAreaFromOutput(output));
        _selectionService.Select(new StudioSelectionState(
            StudioSelectionKind.Output,
            output.Id,
            output.DisplayName,
            output.TypeId,
            new StudioDisplayNameService().GetOutputTypeName(output.TypeId),
            AssignedSceneName(output),
            output.Destination,
            output.Codec,
            output.Bitrate,
            output.Secret));
    }

    private void SelectSource(StudioSource source)
    {
        ClearLayerSelection();
        ClearProjectSelection();
        _selectedSource = source;
        _selectedOutput = null;
        SelectedLayer = null;
        SelectedProjectItem = null;
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
        AddSelectedSourceToCurrentSceneCommand.NotifyCanExecuteChanged();
        Inspector.SelectedPage = new SourceInspectorViewModel(
            source,
            CurrentScene?.DisplayName ?? "cena atual",
            AddSelectedSourceToCurrentSceneCommand,
            ReconnectSourceCommand);
        _selectionService.Select(new StudioSelectionState(
            StudioSelectionKind.Source,
            source.Id,
            source.DisplayName,
            source.TypeId,
            new StudioDisplayNameService().GetSourceTypeName(source.TypeId),
            source.Endpoint));
        RebuildProjectExplorer(source.Id);
    }

    private void ClearLayerSelectionAndShowScene()
    {
        ClearLayerSelection();
        SelectedLayer = null;
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
        if (CurrentScene is not null)
        {
            Inspector.SelectedPage = new SceneInspectorViewModel(CurrentScene, LinkedOutputs(CurrentScene));
        }
    }

    private void RebuildAll()
    {
        RebuildProjectExplorer();
        RebuildSceneLayers();
        RebuildProductionOutputs();
        RebuildSceneOutputRows();
    }

    private void RebuildSceneLayers()
    {
        if (CurrentScene is null)
        {
            Replace(BottomWorkbench.Layers, Array.Empty<LayerItemViewModel>());
            Replace(Preview.Layers, Array.Empty<LayerItemViewModel>());
            return;
        }

        var layers = CurrentScene.Layers
            .OrderByDescending(layer => layer.Order)
            .Select(layer => new LayerItemViewModel(layer, GetLayerIcon(layer.Type, layer.SourceId)))
            .ToArray();

        foreach (var layer in layers)
        {
            AttachLayerCommands(layer);
        }

        Replace(BottomWorkbench.Layers, layers);
        Replace(Preview.Layers, layers);
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
    }

    private void RebuildProjectExplorer(string? selectedId = null)
    {
        var selected = selectedId ?? CurrentScene?.Id;
        var groups = BuildProjectGroups();
        foreach (var item in groups.SelectMany(group => group.Items))
        {
            item.SelectCommand = SelectProjectItemCommand;
            item.IsSelected = item.Id == selected;
        }

        Replace(ProjectExplorer.Groups, groups);
        RebuildExplorerCards(selected);
        ProjectExplorer.ApplyFilter();
        ProjectExplorer.SelectFromOwner(FindProjectItem(selected));
    }

    private void RebuildExplorerCards(string? selectedId)
    {
        var scenes = _document.Scenes.Select(scene =>
        {
            var card = new SceneCardViewModel(
                scene.Id,
                scene.DisplayName,
                $"{scene.Canvas.Width:0}×{scene.Canvas.Height:0} • {scene.Canvas.FrameRate:0.##} fps",
                scene.IsProgram ? "Principal" : string.Empty,
                SceneOutputsLabel(scene),
                scene.IsProgram,
                scene.Id == CurrentScene?.Id)
            {
                IsSelected = scene.Id == selectedId
            };
            card.SelectCommand = new RelayCommand(() => SelectSceneCard(card));
            return card;
        }).ToArray();

        var sources = _document.Sources.Select(source =>
        {
            var card = new SourceCardViewModel(
                source.Id,
                source.DisplayName,
                $"{source.Endpoint} • {source.Metadata}",
                source.Endpoint,
                SourceBadge(source),
                GetSourceIcon(source.TypeId),
                source.Health)
            {
                IsSelected = source.Id == selectedId
            };
            card.SelectCommand = new RelayCommand(() => SelectSourceCard(card));
            return card;
        }).ToArray();

        var outputs = _document.Outputs.Select(output =>
        {
            var card = new OutputCardViewModel(
                output.Id,
                output.DisplayName,
                OutputStateText(output),
                $"Cena atual: {AssignedSceneName(output)}",
                $"Transição: {TransitionText(output)}",
                $"{output.Codec} • {output.Bitrate}",
                OutputBadge(output),
                GetOutputIcon(output.TypeId),
                OutputHealth(output),
                output.IsConfigured,
                output.IsLive,
                RouteActionText(output),
                new RelayCommand(() => OpenSendSceneDialog(output.Id)),
                new RelayCommand(() => SelectOutput(output)))
            {
                IsSelected = output.Id == selectedId
            };
            card.SelectCommand = new RelayCommand(() => SelectOutputCard(card));
            return card;
        }).ToArray();

        Replace(ProjectExplorer.Scenes, scenes);
        Replace(ProjectExplorer.Sources, sources);
        Replace(ProjectExplorer.Outputs, outputs);
    }

    private void RebuildProductionOutputs()
    {
        var cards = _document.Outputs.Select(output => new ProductionOutputCardViewModel(
            output.Id,
            output.DisplayName,
            GetOutputIcon(output.TypeId),
            AssignedSceneName(output),
            OutputStateText(output),
            TransitionText(output),
            output.IsLive || output.State == StudioOutputState.Live,
            output.IsRecording || output.State == StudioOutputState.Recording,
                output.IsConfigured,
                RouteActionText(output),
                new RelayCommand(() => OpenSendSceneDialog(output.Id)),
                new RelayCommand(() => SelectOutput(output)))).ToArray();

        Replace(Production.Outputs, cards);
    }

    private void RebuildSceneOutputRows()
    {
        if (CurrentScene is null)
        {
            Replace(BottomWorkbench.SceneOutputs, Array.Empty<SceneOutputRouteViewModel>());
            return;
        }

        var rows = _document.Outputs
            .Where(output => output.AssignedSceneId == CurrentScene.Id)
            .Select(output => new SceneOutputRouteViewModel(
                output.Id,
                output.DisplayName,
                CurrentScene.DisplayName,
                OutputStateText(output),
                TransitionText(output),
                new RelayCommand(() => OpenSendSceneDialog(output.Id))))
            .ToArray();

        Replace(BottomWorkbench.SceneOutputs, rows);
    }

    private IReadOnlyList<ProjectTreeGroupViewModel> BuildProjectGroups()
    {
        var scenes = _document.Scenes
            .Select(scene => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Scene,
                scene.DisplayName,
                $"{scene.Canvas.Width:0}×{scene.Canvas.Height:0} • {scene.Canvas.FrameRate:0.##} fps",
                StudioIconKind.Scene,
                scene.IsProgram ? "Principal" : string.Empty,
                id: scene.Id,
                typeId: "scene.canvas",
                detail: SceneOutputsLabel(scene)) { IsActive = scene.Id == CurrentScene?.Id })
            .ToArray();

        return new[]
        {
            new ProjectTreeGroupViewModel("Cenas", scenes)
        };
    }

    private void ClearProjectSelection()
    {
        foreach (var item in ProjectExplorer.Groups.SelectMany(group => group.Items))
        {
            item.IsSelected = false;
        }

        foreach (var card in ProjectExplorer.Scenes.Cast<ProjectCardViewModel>()
            .Concat(ProjectExplorer.Sources)
            .Concat(ProjectExplorer.Outputs))
        {
            card.IsSelected = false;
        }
    }

    private void ClearLayerSelection()
    {
        foreach (var layer in BottomWorkbench.Layers)
        {
            layer.IsSelected = false;
        }
    }

    private StudioSelectionState CreateSelection(ProjectTreeItemViewModel item)
    {
        return new StudioSelectionState(
            StudioSelectionKind.Scene,
            item.Id,
            item.Name,
            item.TypeId,
            item.Metadata,
            item.Detail);
    }

    private void ApplyProjectDocument()
    {
        TitleBar.ProjectName = _document.HasUnsavedChanges ? $"{_document.DisplayName} *" : _document.DisplayName;
        TitleBar.WorkspaceState = IsStreaming ? "Ao vivo" : IsRecording ? $"Gravando {_outputService.RecordingElapsed:hh\\:mm\\:ss}" : "Prévia pronta";
        Toolbar.StateBadge = CurrentScene?.IsProgram == true ? "Cena principal" : "Cena em edição";
        UpdateStatusBarSummary();
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (_outputService.RecordingState == StudioOutputUiState.Running)
        {
            ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
            ApplyProjectDocument();
        }
    }

    private void OnOutputStatusChanged(object? sender, StudioOutputStatusChangedEventArgs e)
    {
        ApplyOutputState(e.StreamingState, e.RecordingState);
    }

    private void ApplyOutputState(StudioOutputUiState streamingState, StudioOutputUiState recordingState)
    {
        Toolbar.StreamingState = GetStreamingOutput() is null ? StudioOutputUiState.NotConfigured : streamingState;
        Toolbar.RecordingState = GetRecordingOutput() is null ? StudioOutputUiState.NotConfigured : recordingState;
        Toolbar.StreamButtonText = Toolbar.StreamingState switch
        {
            StudioOutputUiState.Starting => "Conectando...",
            StudioOutputUiState.Running => "Ao vivo",
            StudioOutputUiState.Stopping => "Encerrando...",
            StudioOutputUiState.Error => "Erro na transmissão",
            StudioOutputUiState.NotConfigured => "Configurar transmissão",
            _ => "Transmitir"
        };
        Toolbar.RecordingButtonText = Toolbar.RecordingState switch
        {
            StudioOutputUiState.Starting => "Iniciando gravação...",
            StudioOutputUiState.Running => $"Gravando {_outputService.RecordingElapsed:hh\\:mm\\:ss}",
            StudioOutputUiState.Stopping => "Parando...",
            StudioOutputUiState.Error => "Erro na gravação",
            StudioOutputUiState.NotConfigured => "Configurar gravação",
            _ => "Gravar"
        };
        StatusBar.OutputText = OutputSummary();
        StatusBar.LiveText = IsStreaming ? "Ao vivo" : string.Empty;
        StatusBar.RecordingText = IsRecording ? $"Gravando {_outputService.RecordingElapsed:hh\\:mm\\:ss}" : string.Empty;
        UpdateStatusBarSummary();
        ApplyProjectDocument();
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(IsRecording));
        ToggleStreamingCommand.NotifyCanExecuteChanged();
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged(object? sender, StudioSelectionChangedEventArgs e)
    {
        CurrentSelection = e.Selection;
        StatusBar.StatusText = e.Selection.Kind switch
        {
            StudioSelectionKind.Layer => $"Camada selecionada: {e.Selection.DisplayName}",
            StudioSelectionKind.Scene => $"Cena selecionada: {e.Selection.DisplayName}",
            StudioSelectionKind.Source => $"Fonte selecionada: {e.Selection.DisplayName}",
            StudioSelectionKind.Output => $"Saída selecionada: {e.Selection.DisplayName}",
            _ => $"Selecionado: {e.Selection.DisplayName}"
        };
    }

    private void AttachLayerCommands(LayerItemViewModel layer)
    {
        layer.SelectCommand = SelectLayerCommand;
        layer.ToggleVisibilityCommand = ToggleLayerVisibilityCommand;
        layer.ToggleLockCommand = ToggleLayerLockCommand;
        layer.MoveUpCommand = MoveLayerUpCommand;
        layer.MoveDownCommand = MoveLayerDownCommand;
        AttachEffectCommands(layer.Effects);
    }

    private void AttachEffectCommands(IEnumerable<EffectItemViewModel> effects)
    {
        foreach (var effect in effects)
        {
            effect.ToggleEnabledCommand = ToggleEffectEnabledCommand;
        }
    }

    private void RefreshLayerOrder()
    {
        for (var i = 0; i < BottomWorkbench.Layers.Count; i++)
        {
            BottomWorkbench.Layers[i].Order = BottomWorkbench.Layers.Count - i;
        }
    }

    private void OnPreviewLayerSelectionRequested(object? sender, LayerSelectionRequestedEventArgs e)
    {
        SelectLayer(e.Layer);
    }

    private void SetStatus(string message)
    {
        _diagnosticsService.Append("INFO", "Studio", message);
        StatusBar.StatusText = message;
        StatusBar.SceneText = CurrentScene is null ? "Sem cena" : $"Cena {CurrentScene.DisplayName}";
        StatusBar.OutputText = OutputSummary();
        UpdateStatusBarSummary();
    }

    private void UpdateStatusBarSummary()
    {
        var scene = CurrentScene?.DisplayName ?? "Sem cena";
        StatusBar.CenterText = $"Cena: {scene} | Saídas: {OutputSummary()} | Prévia: {Preview.FrameRate} | 0 quadros descartados";
    }

    private IEnumerable<StudioOutput> LinkedOutputs(StudioScene scene)
    {
        return _document.Outputs.Where(output => output.AssignedSceneId == scene.Id);
    }

    private void ApplyPreviewSafeAreaForScene(StudioScene scene)
    {
        var output = ResolvePreviewOutputForScene(scene);
        if (output is not null)
        {
            ApplyPreviewSafeAreaFromOutput(output);
            return;
        }

        Preview.SetSafeAreaProfile(5, LocalizationManager.Instance["Preview_SafeArea_NoOutput"]);
    }

    private void ApplyPreviewSafeAreaFromOutput(StudioOutput output)
    {
        Preview.SetSafeAreaProfile(output.SafeArea.MarginPercent, output.DisplayName);
    }

    private StudioOutput? ResolvePreviewOutputForScene(StudioScene scene)
    {
        return LinkedOutputs(scene).FirstOrDefault(output => output.TypeId == "output.preview")
            ?? LinkedOutputs(scene).FirstOrDefault();
    }

    private StudioOutput? GetStreamingOutput()
    {
        return _document.Outputs.FirstOrDefault(output => output.IsEnabled && output.IsConfigured && output.TypeId == "output.rtmp");
    }

    private StudioOutput? GetRecordingOutput()
    {
        return _document.Outputs.FirstOrDefault(output => output.IsEnabled && output.IsConfigured && output.TypeId == "output.file.mp4");
    }

    private string OutputSummary()
    {
        var configured = _document.Outputs.Count(output => output.IsConfigured);
        return $"{configured}/{_document.Outputs.Count} saídas configuradas";
    }

    private IEnumerable<StudioDockPanelViewModel> DockPanels()
    {
        yield return NavigationDock;
        yield return ProductionDock;
        yield return PropertiesDock;
        yield return WorkbenchDock;
    }

    private void ApplyLayoutDocument(StudioLayoutDocument document)
    {
        ApplyPanelLayout(NavigationDock, document.Layout.Panels, "navigation");
        ApplyPanelLayout(ProductionDock, document.Layout.Panels, "production");
        ApplyPanelLayout(PropertiesDock, document.Layout.Panels, "properties");
        ApplyPanelLayout(WorkbenchDock, document.Layout.Panels, "layers");
    }

    private static void ApplyPanelLayout(
        StudioDockPanelViewModel panel,
        IReadOnlyDictionary<string, StudioPanelLayoutState> panels,
        string key)
    {
        if (!panels.TryGetValue(key, out var state))
        {
            return;
        }

        panel.IsVisible = state.Visible;
        panel.IsCollapsed = state.Collapsed;
        panel.IsFloating = state.Floating;
    }

    private void SaveLayoutDocument()
    {
        _layoutDocument.Layout.Panels["navigation"] = CapturePanelLayout(NavigationDock);
        _layoutDocument.Layout.Panels["production"] = CapturePanelLayout(ProductionDock);
        _layoutDocument.Layout.Panels["properties"] = CapturePanelLayout(PropertiesDock);
        _layoutDocument.Layout.Panels["layers"] = CapturePanelLayout(WorkbenchDock);
        _layoutService.Save(_layoutDocument);
    }

    private static StudioPanelLayoutState CapturePanelLayout(StudioDockPanelViewModel panel)
    {
        return new StudioPanelLayoutState
        {
            Visible = panel.IsVisible,
            Collapsed = panel.IsCollapsed,
            Floating = panel.IsFloating
        };
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable? dockable)
    {
        if (dockable is null)
        {
            yield break;
        }

        yield return dockable;
        if (dockable is not IDock dock || dock.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var descendant in EnumerateDockables(child))
            {
                yield return descendant;
            }
        }
    }

    private string AssignedSceneName(StudioOutput output)
    {
        return _document.Scenes.FirstOrDefault(scene => scene.Id == output.AssignedSceneId)?.DisplayName ?? "Sem cena";
    }

    private string SceneOutputsLabel(StudioScene scene)
    {
        var linked = LinkedOutputs(scene).Select(output => output.DisplayName).ToArray();
        return linked.Length == 0 ? "Saídas: nenhuma" : $"Saídas: {string.Join(", ", linked)}";
    }

    private string TransitionText(StudioOutput output)
    {
        var transition = _document.Transitions.FirstOrDefault(item => item.Id == output.DefaultTransitionId);
        var name = transition?.DisplayName ?? "Corte rápido";
        return output.TransitionDurationMs <= 0 ? name : $"{name} {output.TransitionDurationMs} ms";
    }

    private static string RouteActionText(StudioOutput output)
    {
        return output.IsLive || output.State == StudioOutputState.Live
            ? "Transicionar cena"
            : "Alterar cena";
    }

    private static string OutputStateText(StudioOutput output)
    {
        if (output.HasPendingSceneUpdate)
        {
            return "Atualização disponível";
        }

        if (!output.IsConfigured)
        {
            return "Não configurada";
        }

        return output.State switch
        {
            StudioOutputState.Live => "Ao vivo",
            StudioOutputState.Recording => "Gravando",
            StudioOutputState.Warning => "Atenção",
            StudioOutputState.Offline => "Offline",
            StudioOutputState.Planned => "Planejada",
            _ => "Pronta"
        };
    }

    private ProjectTreeItemViewModel? FindProjectItem(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return ProjectExplorer.Groups.SelectMany(group => group.Items).FirstOrDefault(item => item.Id == id);
    }

    private static string SourceMetadata(string typeId)
    {
        return typeId switch
        {
            "source.webcam" => "Câmera / 1080p60",
            "source.desktop" => "Tela / 1440p60",
            "source.image" => "Imagem / arquivo",
            "source.media" => "Vídeo / arquivo",
            "source.text" => "Texto editável",
            "source.solid" => "Cor sólida",
            _ => "Fonte planejada"
        };
    }

    private static string SourceEndpoint(string typeId)
    {
        return typeId switch
        {
            "source.webcam" => "Logitech BRIO",
            "source.desktop" => "Monitor 1",
            "source.image" => "assets/imagem.png",
            "source.media" => "media/video.mp4",
            "source.text" => "Texto criado no Studio",
            "source.solid" => "#101823",
            _ => "Planejado"
        };
    }

    private static string SourceTypeToLayerType(string typeId)
    {
        return typeId switch
        {
            "source.text" => "Text",
            "source.image" => "Image",
            "source.solid" => "Solid",
            _ => "Source"
        };
    }

    private static StudioIconKind GetOutputIcon(string typeId)
    {
        return typeId switch
        {
            "output.preview" => StudioIconKind.Preview,
            "output.file.mp4" => StudioIconKind.Record,
            "output.rtmp" => StudioIconKind.Stream,
            _ => StudioIconKind.Output
        };
    }

    private static StudioIconKind GetSourceIcon(string typeId)
    {
        return typeId switch
        {
            "source.webcam" => StudioIconKind.Camera,
            "source.desktop" => StudioIconKind.Desktop,
            "source.image" => StudioIconKind.Image,
            "source.text" => StudioIconKind.Text,
            "source.media" => StudioIconKind.Video,
            _ => StudioIconKind.Source
        };
    }

    private static string SourceBadge(StudioSource source)
    {
        return source.Health switch
        {
            StudioHealthState.Planned => "Planejada",
            StudioHealthState.Warning => "Atenção",
            StudioHealthState.Error => "Erro",
            StudioHealthState.Disabled => "Inativa",
            _ => source.TypeId is "source.webcam" or "source.desktop" ? "LIVE" : string.Empty
        };
    }

    private static string OutputBadge(StudioOutput output)
    {
        if (output.HasPendingSceneUpdate)
        {
            return "ATUALIZAR";
        }

        if (output.IsLive || output.State == StudioOutputState.Live)
        {
            return "AO VIVO";
        }

        if (output.IsRecording || output.State == StudioOutputState.Recording)
        {
            return "GRAVANDO";
        }

        return output.IsConfigured ? "PRONTA" : "CONFIGURAR";
    }

    private static StudioHealthState OutputHealth(StudioOutput output)
    {
        if (!output.IsConfigured)
        {
            return StudioHealthState.Warning;
        }

        return output.State switch
        {
            StudioOutputState.Live or StudioOutputState.Recording or StudioOutputState.Running => StudioHealthState.Healthy,
            StudioOutputState.Warning => StudioHealthState.Warning,
            StudioOutputState.Offline => StudioHealthState.Error,
            StudioOutputState.Planned => StudioHealthState.Planned,
            _ => StudioHealthState.Healthy
        };
    }

    private static StudioIconKind GetLayerIcon(string layerType, string sourceId)
    {
        if (layerType == "Text")
        {
            return StudioIconKind.Text;
        }

        if (layerType == "Image")
        {
            return StudioIconKind.Image;
        }

        if (layerType == "Solid")
        {
            return StudioIconKind.Source;
        }

        return sourceId.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? StudioIconKind.Desktop
            : StudioIconKind.Camera;
    }
}
