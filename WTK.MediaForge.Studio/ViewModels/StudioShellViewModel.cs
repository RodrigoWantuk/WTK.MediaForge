using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class StudioShellViewModel : ViewModelBase
{
    private readonly IStudioProjectService _projectService;
    private readonly IStudioOutputService _outputService;
    private readonly IStudioDiagnosticsService _diagnosticsService;
    private readonly IStudioSelectionService _selectionService;
    private readonly IStudioUiTimer _uiTimer;
    private StudioDocument _document = StudioMockDocumentFactory.Create();
    private ProjectTreeItemViewModel? _selectedProjectItem;
    private LayerItemViewModel? _selectedLayer;
    private StudioScene? _currentScene;
    private StudioSource? _selectedSource;
    private StudioOutput? _selectedOutput;
    private StudioSelectionState _currentSelection = StudioSelectionState.None;

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
        Preview.LayerSelectionRequested += OnPreviewLayerSelectionRequested;

        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        AddSourceCommand = new RelayCommand(OpenAddSourceDialog);
        AddSceneCommand = new RelayCommand(OpenAddSceneDialog);
        ConfigureOutputCommand = new RelayCommand(OpenConfigureOutputDialog);
        SettingsCommand = new RelayCommand(() => ShowDialog("Configuracoes", "Preferencias do Studio ficarao aqui no proximo milestone.", "settings", "Fechar"));
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
        ReconnectSourceCommand = new RelayCommand(() => SetStatus("Reconexao de fonte agendada no mock."));
        ConfirmDialogCommand = new RelayCommand(ConfirmDialog);
        CancelDialogCommand = new RelayCommand(CloseDialog);

        _outputService.StatusChanged += OnOutputStatusChanged;
        _selectionService.SelectionChanged += OnSelectionChanged;
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        LoadDesignData(_document, _diagnosticsService.Items);
        ApplyProjectDocument();
        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
    }

    public TitleBarViewModel TitleBar { get; } = new();

    public ToolbarViewModel Toolbar { get; } = new();

    public ProjectExplorerViewModel ProjectExplorer { get; } = new();

    public PreviewCanvasViewModel Preview { get; } = new();

    public InspectorHostViewModel Inspector { get; } = new();

    public BottomWorkbenchViewModel BottomWorkbench { get; }

    public StatusBarViewModel StatusBar { get; } = new();

    public StudioDialogViewModel Dialog { get; } = new();

    public IAsyncRelayCommand NewProjectCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IAsyncRelayCommand SaveProjectCommand { get; }

    public ICommand AddSourceCommand { get; }

    public ICommand AddSceneCommand { get; }

    public ICommand ConfigureOutputCommand { get; }

    public ICommand SettingsCommand { get; }

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
        RebuildProjectExplorer();
        SelectScene(_document.Scenes.FirstOrDefault(scene => scene.Id == _document.SelectedSceneId) ?? _document.Scenes.First());

        var initialLayer = BottomWorkbench.Layers.FirstOrDefault(layer => layer.Name == "Webcam")
            ?? BottomWorkbench.Layers.FirstOrDefault();
        if (initialLayer is not null)
        {
            SelectLayer(initialLayer);
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
        ProjectExplorer.SelectFromOwner(item);
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
        Replace(BottomWorkbench.Effects, Array.Empty<EffectItemViewModel>());
        BottomWorkbench.EffectsContextTitle = "Selecione uma camada";
        BottomWorkbench.NotifyEffectsChanged();

        switch (item.Kind)
        {
            case StudioProjectItemKind.Scene:
                SelectScene(_document.Scenes.First(scene => scene.Id == item.Id));
                Inspector.SelectedPage = new SceneInspectorViewModel(CurrentScene!, LinkedOutputs(CurrentScene!));
                _selectionService.Select(CreateSelection(item));
                break;
            case StudioProjectItemKind.Source:
                _selectedSource = _document.Sources.First(source => source.Id == item.Id);
                _selectedOutput = null;
                Inspector.SelectedPage = new SourceInspectorViewModel(_selectedSource, CurrentScene?.DisplayName ?? "Cena atual", AddSelectedSourceToCurrentSceneCommand, ReconnectSourceCommand);
                _selectionService.Select(CreateSelection(item));
                break;
            case StudioProjectItemKind.Output:
                _selectedSource = null;
                _selectedOutput = _document.Outputs.First(output => output.Id == item.Id);
                Inspector.SelectedPage = new OutputInspectorViewModel(_selectedOutput, _document.Scenes, SyncOutputRoutes);
                _selectionService.Select(CreateSelection(item));
                break;
            case StudioProjectItemKind.Preset:
                Inspector.SelectedPage = new PresetInspectorViewModel(item.Name, item.Metadata);
                _selectionService.Select(CreateSelection(item));
                break;
            case StudioProjectItemKind.Package:
                Inspector.SelectedPage = new PackageInspectorViewModel(item.Name, item.Metadata);
                _selectionService.Select(CreateSelection(item));
                break;
        }
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

        _selectedSource = null;
        _selectedOutput = null;
        SelectedProjectItem = null;
        SelectedLayer = layer;
        ProjectExplorer.SelectFromOwner(null);
        BottomWorkbench.SelectLayerFromOwner(layer);
        Preview.SelectLayerFromOwner(layer);
        Replace(BottomWorkbench.Effects, layer.Effects);
        AttachEffectCommands(layer.Effects);
        BottomWorkbench.EffectsContextTitle = $"Efeitos de {layer.Name}";
        BottomWorkbench.NotifyEffectsChanged();
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
        output.AssignedSceneId = sceneId;
        output.IsConfigured = true;
        SyncOutputRoutes();
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
                new BottomTabViewModel(StudioBottomTabKind.Effects, "Efeitos"),
                new BottomTabViewModel(StudioBottomTabKind.Outputs, "Saidas")
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
        _document.DisplayName = "Projeto sem titulo";
        _document.HasUnsavedChanges = true;
        LoadDesignData(_document, _diagnosticsService.Items);
        ApplyProjectDocument();
        SetStatus("Novo projeto mock criado.");
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
        SetStatus("Projeto salvo em pacote mock.");
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
        output.State = IsStreaming ? StudioOutputState.Live : StudioOutputState.Running;
        SyncOutputRoutes();
        SetStatus(IsStreaming ? $"Transmitindo {AssignedSceneName(output)}." : "Transmissao encerrada.");
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
        output.State = IsRecording ? StudioOutputState.Recording : StudioOutputState.Running;
        SyncOutputRoutes();
        SetStatus(IsRecording ? $"Gravando {AssignedSceneName(output)}." : "Gravacao encerrada.");
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
        ShowDialog(
            "Adicionar fonte",
            $"Cria uma fonte mock e adiciona uma camada na cena {CurrentScene?.DisplayName ?? "atual"}.",
            "source",
            "Adicionar");
    }

    private void OpenAddSceneDialog()
    {
        ShowDialog("Adicionar cena", "Cria uma cena vazia pronta para receber fontes.", "scene", "Criar cena");
    }

    private void OpenConfigureOutputDialog()
    {
        var output = _selectedOutput ?? _document.Outputs.FirstOrDefault(item => !item.IsConfigured) ?? _document.Outputs.FirstOrDefault();
        if (output is not null)
        {
            _selectedOutput = output;
        }

        ShowDialog(
            "Configurar saida",
            output is null ? "Nenhuma saida disponivel no projeto." : $"Ajusta a saida {output.DisplayName} para usar a cena atual.",
            "output",
            "Configurar");
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
            case "source":
                AddMockSourceToCurrentScene();
                break;
            case "scene":
                AddMockScene();
                break;
            case "output":
                ConfigureSelectedOutput();
                break;
        }

        CloseDialog();
    }

    private void CloseDialog()
    {
        Dialog.IsOpen = false;
    }

    private void AddSelectedSourceToCurrentScene()
    {
        if (_selectedSource is null)
        {
            return;
        }

        AddSourceLayerToCurrentScene(_selectedSource);
    }

    private void AddMockSourceToCurrentScene()
    {
        var index = _document.Sources.Count(source => source.TypeId == "source.text") + 1;
        var source = new StudioSource
        {
            Id = $"source-text-overlay-{index}",
            DisplayName = $"Texto {index}",
            TypeId = "source.text",
            Metadata = "Texto gerado",
            Endpoint = "Criado no Studio"
        };
        _document.Sources.Add(source);
        RebuildProjectExplorer();
        AddSourceLayerToCurrentScene(source);
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
            Description = "Disponivel para ajustar fundo verde nesta camada.",
            IsEnabled = false
        });

        CurrentScene.Layers.Add(layer);
        _document.HasUnsavedChanges = true;
        RebuildSceneLayers();
        var selected = BottomWorkbench.Layers.First(item => item.Id == layer.Id);
        SelectLayer(selected);
        ApplyProjectDocument();
        SetStatus($"{source.DisplayName} adicionada a {CurrentScene.DisplayName}.");
    }

    private void AddMockScene()
    {
        var count = _document.Scenes.Count + 1;
        var scene = new StudioScene
        {
            Id = $"scene-{count}",
            DisplayName = $"Cena {count}",
            Metadata = "1920 x 1080 / 60 fps"
        };
        _document.Scenes.Add(scene);
        _document.HasUnsavedChanges = true;
        RebuildProjectExplorer();
        SelectScene(scene);
        var item = FindProjectItem(scene.Id);
        if (item is not null)
        {
            SelectProjectItem(item);
        }

        ApplyProjectDocument();
        SetStatus($"{scene.DisplayName} criada.");
    }

    private void ConfigureSelectedOutput()
    {
        if (_selectedOutput is null || CurrentScene is null)
        {
            return;
        }

        _selectedOutput.AssignedSceneId = CurrentScene.Id;
        _selectedOutput.IsConfigured = true;
        _selectedOutput.IsEnabled = true;
        if (_selectedOutput.State == StudioOutputState.Planned)
        {
            _selectedOutput.State = StudioOutputState.Running;
        }

        SyncOutputRoutes();
        var item = FindProjectItem(_selectedOutput.Id);
        if (item is not null)
        {
            SelectProjectItem(item);
        }

        SetStatus($"{_selectedOutput.DisplayName} roteada para {CurrentScene.DisplayName}.");
    }

    private void SelectScene(StudioScene scene)
    {
        CurrentScene = scene;
        _document.SelectedSceneId = scene.Id;
        Preview.SceneName = scene.DisplayName;
        Preview.SetCanvas(scene.Canvas.Width, scene.Canvas.Height, scene.Canvas.FrameRate, scene.IsProgram);
        RebuildSceneLayers();
        RebuildProjectExplorer(scene.Id);
        StatusBar.SceneText = $"Cena {scene.DisplayName}";
        StatusBar.OutputText = OutputSummary();
        AddSelectedSourceToCurrentSceneCommand.NotifyCanExecuteChanged();
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
        Replace(BottomWorkbench.Effects, Array.Empty<EffectItemViewModel>());
        BottomWorkbench.EffectsContextTitle = "Selecione uma camada";
        BottomWorkbench.NotifyEffectsChanged();
        BottomWorkbench.SelectLayerFromOwner(null);
        Preview.SelectLayerFromOwner(null);
    }

    private void RebuildProjectExplorer(string? selectedId = null)
    {
        var selected = selectedId ?? SelectedProjectItem?.Id;
        var groups = BuildProjectGroups();
        foreach (var item in groups.SelectMany(group => group.Items))
        {
            item.SelectCommand = SelectProjectItemCommand;
            item.IsSelected = item.Id == selected;
        }

        Replace(ProjectExplorer.Groups, groups);
        ProjectExplorer.ApplyFilter();
        ProjectExplorer.SelectFromOwner(FindProjectItem(selected));
        RebuildOutputRows();
    }

    private void RebuildOutputRows()
    {
        var rows = _document.Outputs.Select(output =>
        {
            var row = new OutputMonitorItemViewModel(
                output.Id,
                output.DisplayName,
                output.State,
                AssignedSceneName(output),
                output.Destination,
                output.Bitrate,
                output.IsConfigured ? "Configurada" : "Falta configurar",
                output.TypeId);
            row.SelectCommand = new RelayCommand(() =>
            {
                var item = FindProjectItem(output.Id);
                if (item is not null)
                {
                    SelectProjectItem(item);
                }
            });
            return row;
        }).ToArray();

        Replace(BottomWorkbench.Outputs, rows);
    }

    private IReadOnlyList<ProjectTreeGroupViewModel> BuildProjectGroups()
    {
        var scenes = _document.Scenes
            .Select(scene => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Scene,
                scene.DisplayName,
                scene.Metadata,
                StudioIconKind.Scene,
                scene.IsProgram ? "PRINCIPAL" : string.Empty,
                id: scene.Id,
                typeId: "scene.canvas",
                detail: string.Join(", ", LinkedOutputs(scene).Select(output => output.DisplayName))) { IsActive = scene.Id == CurrentScene?.Id })
            .ToArray();

        var sources = _document.Sources
            .Select(source => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Source,
                source.DisplayName,
                source.Metadata,
                GetSourceIcon(source.TypeId),
                SourceBadge(source),
                source.Health,
                source.Id,
                source.TypeId,
                source.Endpoint))
            .ToArray();

        var outputs = _document.Outputs
            .Select(output => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Output,
                output.DisplayName,
                OutputMetadata(output),
                GetOutputIcon(output.TypeId),
                output.IsConfigured ? OutputBadge(output) : "CONFIG",
                output.IsConfigured ? output.State == StudioOutputState.Planned ? StudioHealthState.Planned : StudioHealthState.Healthy : StudioHealthState.Warning,
                output.Id,
                output.TypeId,
                AssignedSceneName(output),
                output.Destination,
                output.Codec,
                output.Bitrate,
                output.Secret))
            .ToArray();

        var presets = _document.Presets
            .Select(preset => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Preset,
                preset.DisplayName,
                preset.Metadata,
                StudioIconKind.Preset,
                id: preset.Id,
                typeId: preset.TypeId))
            .ToArray();

        var packages = _document.Packages
            .Select(pkg => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Package,
                pkg.DisplayName,
                pkg.Metadata,
                StudioIconKind.Package,
                pkg.Id == "package-brand-kit" ? "v2" : string.Empty,
                id: pkg.Id,
                typeId: pkg.TypeId))
            .ToArray();

        return new[]
        {
            new ProjectTreeGroupViewModel("Cenas", scenes),
            new ProjectTreeGroupViewModel("Fontes", sources),
            new ProjectTreeGroupViewModel("Saidas", outputs),
            new ProjectTreeGroupViewModel("Presets", presets),
            new ProjectTreeGroupViewModel("Pacotes", packages)
        };
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

    private StudioSelectionState CreateSelection(ProjectTreeItemViewModel item)
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
        TitleBar.ProjectName = _document.HasUnsavedChanges ? $"{_document.DisplayName} *" : _document.DisplayName;
        TitleBar.WorkspaceState = "Modo edicao visual";
        Toolbar.StateBadge = CurrentScene?.IsProgram == true ? "Cena principal" : "Cena em edicao";
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (_outputService.RecordingState == StudioOutputUiState.Running)
        {
            ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
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
            StudioOutputUiState.Error => "Erro na transmissao",
            StudioOutputUiState.NotConfigured => "Configurar transmissao",
            _ => "Transmitir"
        };
        Toolbar.RecordingButtonText = Toolbar.RecordingState switch
        {
            StudioOutputUiState.Starting => "Iniciando gravacao...",
            StudioOutputUiState.Running => $"Gravando {_outputService.RecordingElapsed:hh\\:mm\\:ss}",
            StudioOutputUiState.Stopping => "Parando...",
            StudioOutputUiState.Error => "Erro na gravacao",
            StudioOutputUiState.NotConfigured => "Configurar gravacao",
            _ => "Gravar"
        };
        StatusBar.OutputText = OutputSummary();
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
            StudioSelectionKind.Output => $"Saida selecionada: {e.Selection.DisplayName}",
            _ => $"Selecionado: {e.Selection.DisplayName}"
        };
    }

    private void SyncOutputRoutes()
    {
        RebuildProjectExplorer(_selectedOutput?.Id ?? SelectedProjectItem?.Id);
        RebuildOutputRows();
        StatusBar.OutputText = OutputSummary();
        if (CurrentScene is not null && CurrentSelection.Kind == StudioSelectionKind.Scene)
        {
            Inspector.SelectedPage = new SceneInspectorViewModel(CurrentScene, LinkedOutputs(CurrentScene));
        }

        ApplyOutputState(_outputService.StreamingState, _outputService.RecordingState);
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
    }

    private IEnumerable<StudioOutput> LinkedOutputs(StudioScene scene)
    {
        return _document.Outputs.Where(output => output.AssignedSceneId == scene.Id);
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
        return $"{configured}/{_document.Outputs.Count} saidas configuradas";
    }

    private string AssignedSceneName(StudioOutput output)
    {
        return _document.Scenes.FirstOrDefault(scene => scene.Id == output.AssignedSceneId)?.DisplayName ?? "Sem cena";
    }

    private string OutputMetadata(StudioOutput output)
    {
        var sceneName = AssignedSceneName(output);
        return output.IsConfigured
            ? $"{sceneName} / {output.Codec} / {output.Bitrate}"
            : $"{sceneName} / falta configurar";
    }

    private static string OutputBadge(StudioOutput output)
    {
        return output.State switch
        {
            StudioOutputState.Live => "LIVE",
            StudioOutputState.Recording => "REC",
            StudioOutputState.Planned => "PLAN",
            StudioOutputState.Warning => "ATENCAO",
            StudioOutputState.Offline => "OFF",
            _ => "ON"
        };
    }

    private static string SourceBadge(StudioSource source)
    {
        return source.TypeId switch
        {
            "source.webcam" => "LIVE",
            "source.desktop" => "TELA",
            _ when source.Health == StudioHealthState.Warning => "BUFFER",
            _ => string.Empty
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

    private static string SourceTypeToLayerType(string typeId)
    {
        return typeId switch
        {
            "source.text" => "Text",
            "source.image" => "Image",
            _ => "Source"
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

        return sourceId.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? StudioIconKind.Desktop
            : StudioIconKind.Camera;
    }
}
