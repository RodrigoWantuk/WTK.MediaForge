using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Services;

public sealed class FakeStudioProjectService : IStudioProjectService
{
    public StudioProjectDocument Current { get; } = new("Produção ao vivo");

    public Task<StudioDocument> NewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename("Projeto sem título", null, true);
        var document = StudioMockDocumentFactory.Create();
        document.DisplayName = Current.DisplayName;
        document.HasUnsavedChanges = true;
        return Task.FromResult(document);
    }

    public Task<StudioDocument> OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename("Projeto carregado", path, false);
        var document = StudioMockDocumentFactory.Create();
        document.DisplayName = Current.DisplayName;
        return Task.FromResult(document);
    }

    public Task SaveAsync(StudioDocument document, string? path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename(Current.DisplayName, path ?? Current.Path ?? "mock-project.mforge.json", false);
        return Task.CompletedTask;
    }
}

public sealed class FakeStudioEngineService : IStudioEngineService
{
    private StudioEngineStatus _currentStatus = new(StudioEngineUiState.Stopped, "Pronto");

    public StudioEngineStatus CurrentStatus => _currentStatus;

    public StudioEngineHealth CurrentHealth { get; private set; } =
        new(StudioEngineUiState.Stopped, "Pronto", DateTimeOffset.UtcNow);

    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<StudioEngineHealthChangedEventArgs>? HealthChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_currentStatus.State is StudioEngineUiState.Running or StudioEngineUiState.Starting)
        {
            return;
        }

        Publish(new StudioEngineStatus(StudioEngineUiState.Starting, "Preparando"));
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        Publish(new StudioEngineStatus(StudioEngineUiState.Running, "Pronto"));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_currentStatus.State is StudioEngineUiState.Stopped or StudioEngineUiState.Stopping)
        {
            return;
        }

        Publish(new StudioEngineStatus(StudioEngineUiState.Stopping, "Finalizando"));
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        Publish(new StudioEngineStatus(StudioEngineUiState.Stopped, "Pronto"));
    }

    private void Publish(StudioEngineStatus status)
    {
        _currentStatus = status;
        StatusChanged?.Invoke(this, new StudioEngineStatusChangedEventArgs(status));
        CurrentHealth = new StudioEngineHealth(status.State, status.Message, DateTimeOffset.UtcNow);
        HealthChanged?.Invoke(this, new StudioEngineHealthChangedEventArgs(CurrentHealth));
    }
}

public sealed class FakeStudioSceneEditRuntimeService : IStudioSceneEditRuntimeService
{
    public bool IsEngineBacked => false;

    public ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(
        StudioDocument document,
        StudioScene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new StudioSceneEditRuntimeSession(Guid.NewGuid().ToString("N"), scene.Id, false));
    }

    public ValueTask TrackLayerVisualStateAsync(
        StudioSceneEditRuntimeSession session,
        StudioLayer layer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layer);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask TrackSceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioDocument document,
        StudioScene originalScene,
        StudioScene draftScene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(originalScene);
        ArgumentNullException.ThrowIfNull(draftScene);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioTransition? transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new StudioSceneEditApplyResult(false, []));
    }

    public ValueTask DiscardSceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeStudioOutputService : IStudioOutputService
{
    private readonly IStudioClock _clock;

    public FakeStudioOutputService(IStudioClock clock)
    {
        _clock = clock;
    }

    public StudioOutputUiState StreamingState { get; private set; } = StudioOutputUiState.Ready;

    public StudioOutputUiState RecordingState { get; private set; } = StudioOutputUiState.Ready;

    public DateTimeOffset? RecordingStartedAt { get; private set; }

    public TimeSpan RecordingElapsed => RecordingState == StudioOutputUiState.Running && RecordingStartedAt is not null
        ? _clock.Now - RecordingStartedAt.Value
        : TimeSpan.Zero;

    public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged;

    public async Task ToggleStreamingAsync(CancellationToken cancellationToken)
    {
        StreamingState = StreamingState == StudioOutputUiState.Running ? StudioOutputUiState.Stopping : StudioOutputUiState.Starting;
        Publish();
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        StreamingState = StreamingState == StudioOutputUiState.Stopping ? StudioOutputUiState.Ready : StudioOutputUiState.Running;
        Publish();
    }

    public async Task ToggleRecordingAsync(CancellationToken cancellationToken)
    {
        RecordingState = RecordingState == StudioOutputUiState.Running ? StudioOutputUiState.Stopping : StudioOutputUiState.Starting;
        Publish();
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        if (RecordingState == StudioOutputUiState.Stopping)
        {
            RecordingState = StudioOutputUiState.Ready;
            RecordingStartedAt = null;
        }
        else
        {
            RecordingState = StudioOutputUiState.Running;
            RecordingStartedAt = _clock.Now;
        }

        Publish();
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StreamingState = StudioOutputUiState.Ready;
        RecordingState = StudioOutputUiState.Ready;
        RecordingStartedAt = null;
        Publish();
        return Task.CompletedTask;
    }

    private void Publish()
    {
        StatusChanged?.Invoke(this, new StudioOutputStatusChangedEventArgs(StreamingState, RecordingState));
    }
}

public sealed class FakeStudioCapabilityService : IStudioCapabilityService
{
    private readonly List<StudioCapabilityDescriptor> _sources =
    [
        new(
            "source.image",
            "Imagem",
            "PNG e JPEG como asset estático",
            StudioIconKind.Image,
            StudioCapabilityStatus.Supported,
            "decode ocorre uma vez no carregamento e segue como textura GPU no produto"),
        new(
            "source.text",
            "Texto",
            "Título, legenda ou tarja",
            StudioIconKind.Text,
            StudioCapabilityStatus.Supported,
            "fonte visual interna da composição"),
        new(
            "source.solid",
            "Cor sólida",
            "Fundo ou shape simples",
            StudioIconKind.Source,
            StudioCapabilityStatus.Supported,
            "fonte visual interna da composição"),
        new(
            "source.desktop",
            "Tela",
            "Capturar monitor ou janela",
            StudioIconKind.Desktop,
            StudioCapabilityStatus.Experimental,
            "desktop duplication existe, mas window capture ainda depende de provider Windows Graphics Capture"),
        new(
            "source.webcam",
            "Webcam",
            "Capturar câmera local",
            StudioIconKind.Camera,
            StudioCapabilityStatus.Unavailable,
            "capability de webcam deve vir da prova Media Foundation/GPU upload da máquina atual"),
        new(
            "source.media",
            "Vídeo",
            "Arquivo MP4 com decode por hardware",
            StudioIconKind.Video,
            StudioCapabilityStatus.Unavailable,
            "decode hardware e decode-to-render precisam estar validados no capability report"),
        new(
            "source.ndi",
            "NDI",
            "Entrada de rede NDI",
            StudioIconKind.Stream,
            StudioCapabilityStatus.Blocked,
            "Standard SDK raw frame-buffer não é aceito como caminho de vídeo contínuo do produto"),
        new(
            "source.rtsp",
            "RTSP/IP",
            "Câmera IP ou stream RTSP",
            StudioIconKind.Camera,
            StudioCapabilityStatus.Planned,
            "adapter de ingestão ainda não foi implementado")
    ];

    private readonly List<StudioCapabilityDescriptor> _outputs =
    [
        new(
            "output.preview",
            "Prévia local",
            "Painel de prévia GPU",
            StudioIconKind.Output,
            StudioCapabilityStatus.Supported,
            "PreviewPanelSink é o caminho de preview GPU validado"),
        new(
            "output.file.mp4",
            "Gravação MP4",
            "Arquivo H.264 em MP4",
            StudioIconKind.Record,
            StudioCapabilityStatus.Unavailable,
            "requer hardware encode, render-to-encode e prova MP4 da máquina atual"),
        new(
            "output.rtmp",
            "RTMP",
            "Transmissão H.264 para servidor RTMP",
            StudioIconKind.Stream,
            StudioCapabilityStatus.Unavailable,
            "requer hardware encode, render-to-encode e prova RTMP da máquina atual"),
        new(
            "output.ndi",
            "NDI",
            "Saída NDI",
            StudioIconKind.Stream,
            StudioCapabilityStatus.Blocked,
            "aguarda prova de caminho GPU-safe para envio NDI"),
        new(
            "output.virtual-camera",
            "Câmera virtual",
            "Dispositivo de câmera virtual",
            StudioIconKind.Camera,
            StudioCapabilityStatus.Planned,
            "adapter de câmera virtual ainda não foi implementado")
    ];

    public IReadOnlyList<StudioCapabilityDescriptor> GetSourceCapabilities() => _sources;

    public IReadOnlyList<StudioCapabilityDescriptor> GetOutputCapabilities() => _outputs;
}

public sealed class SystemStudioClock : IStudioClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public sealed class FakeStudioClock : IStudioClock
{
    public FakeStudioClock(DateTimeOffset? now = null)
    {
        Now = now ?? DateTimeOffset.UnixEpoch;
    }

    public DateTimeOffset Now { get; private set; }

    public void Advance(TimeSpan value)
    {
        Now += value;
    }
}

public sealed class AvaloniaStudioUiTimer : IStudioUiTimer
{
    private readonly DispatcherTimer _timer;

    public AvaloniaStudioUiTimer()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }
}

public sealed class FakeStudioUiTimer : IStudioUiTimer
{
    public event EventHandler? Tick;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void RaiseTick()
    {
        if (IsRunning)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class StudioDiagnosticsService : IStudioDiagnosticsService
{
    private const int MaxItems = 80;

    public StudioDiagnosticsService(IEnumerable<DiagnosticLogItemViewModel>? seedItems = null)
    {
        Items = new ObservableCollection<DiagnosticLogItemViewModel>(seedItems ?? Array.Empty<DiagnosticLogItemViewModel>());
    }

    public ObservableCollection<DiagnosticLogItemViewModel> Items { get; }

    public void Append(string level, string category, string message)
    {
        Items.Insert(0, new DiagnosticLogItemViewModel(DateTime.Now.ToString("HH:mm:ss"), level, message, category));

        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }
}

public sealed class StudioSelectionService : IStudioSelectionService
{
    private StudioSelectionState _current = StudioSelectionState.None;

    public StudioSelectionState Current => _current;

    public event EventHandler<StudioSelectionChangedEventArgs>? SelectionChanged;

    public void Select(StudioSelectionState selection)
    {
        _current = selection;
        SelectionChanged?.Invoke(this, new StudioSelectionChangedEventArgs(selection));
    }
}

public sealed class StudioInspectorPageFactory : IInspectorPageFactory
{
    public InspectorPageViewModel Create(StudioSelectionState selection, ICommand? reconnectCommand)
    {
        var document = StudioMockDocumentFactory.Create();
        return selection.Kind switch
        {
            StudioSelectionKind.Scene => new SceneInspectorViewModel(
                document.Scenes.FirstOrDefault(scene => scene.Id == selection.EntityId) ?? document.Scenes[0],
                document.Outputs),
            StudioSelectionKind.Source => new SourceInspectorViewModel(
                document.Sources.FirstOrDefault(source => source.Id == selection.EntityId) ?? document.Sources[0],
                document.Scenes[0].DisplayName,
                null,
                reconnectCommand),
            StudioSelectionKind.Layer => new LayerInspectorViewModel(selection.DisplayName, selection.Detail),
            StudioSelectionKind.Output => new OutputInspectorViewModel(
                document.Outputs.FirstOrDefault(output => output.Id == selection.EntityId) ?? document.Outputs[0],
                document.Scenes[0].DisplayName,
                document.Transitions,
                null),
            StudioSelectionKind.Preset => new PresetInspectorViewModel(selection.DisplayName, selection.Metadata),
            StudioSelectionKind.Package => new PackageInspectorViewModel(selection.DisplayName, selection.Metadata),
            _ => new EmptyInspectorViewModel()
        };
    }
}

public static class StudioServiceFactory
{
    public static StudioServiceBundle CreateFake(
        IEnumerable<DiagnosticLogItemViewModel>? diagnostics = null,
        IStudioClock? clock = null,
        IStudioEngineService? engineService = null,
        IStudioSceneEditRuntimeService? sceneEditRuntimeService = null,
        IStudioOutputService? outputService = null,
        IStudioLayoutService? layoutService = null,
        IStudioUiTimer? uiTimer = null)
    {
        clock ??= new SystemStudioClock();
        var capabilityService = new FakeStudioCapabilityService();
        return new StudioServiceBundle(
            new FakeStudioProjectService(),
            engineService ?? new FakeStudioEngineService(),
            sceneEditRuntimeService ?? new FakeStudioSceneEditRuntimeService(),
            outputService ?? new FakeStudioOutputService(clock),
            capabilityService,
            new StudioDialogService(capabilityService),
            new StudioUndoRedoService(),
            new StudioShortcutService(),
            layoutService ?? new StudioLayoutService(),
            new StudioDiagnosticsService(diagnostics),
            new StudioSelectionService(),
            new StudioInspectorPageFactory(),
            uiTimer ?? new AvaloniaStudioUiTimer(),
            StudioMockDocumentFactory.Create());
    }
}
