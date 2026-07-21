using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Windows;

namespace WTK.MediaForge.Studio.Services;

public sealed class RuntimeStudioProjectService : IStudioProjectService
{
    private readonly StudioProjectEngineMapper _mapper;
    private readonly string _defaultPath;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private StudioProjectSession? _session;

    public RuntimeStudioProjectService(StudioProjectEngineMapper mapper, string? defaultPath = null)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _defaultPath = Path.GetFullPath(defaultPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WTK MediaForge",
            "mediaforge-project.mforge.json"));
        Current = new StudioProjectDocument("Projeto sem título", _defaultPath, true);
    }

    public StudioProjectDocument Current { get; }

    public async Task<StudioDocument> NewAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = CreateEmptyDocument();
            _session = StudioProjectSession.Create(_mapper, document);
            Current.Rename("Projeto sem título", _defaultPath, true);
            return document;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<StudioDocument> OpenAsync(string path, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resolvedPath = ResolvePath(path);
            var json = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            var result = MediaForgeProjectLoader.LoadFromJson(json);
            if (!result.Validation.IsValid || result.Project is null)
            {
                throw new InvalidDataException(
                    $"O projeto não pôde ser aberto: {string.Join("; ", result.Validation.Issues.Select(static issue => issue.Message))}");
            }

            var displayName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(resolvedPath));
            _session = StudioProjectSession.Open(_mapper, result.Project, displayName);
            var document = _session.Document;
            Current.Rename(document.DisplayName, resolvedPath, false);
            return document;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SaveAsync(StudioDocument document, string? path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resolvedPath = ResolvePath(path ?? Current.Path ?? _defaultPath);
            if (_session is null || !ReferenceEquals(_session.Document, document))
                _session = StudioProjectSession.Create(_mapper, document);
            var project = _session.CreateValidatedSaveSnapshot(document);
            var json = MediaForgeProjectSerializer.Serialize(project);
            var directory = Path.GetDirectoryName(resolvedPath)
                ?? throw new InvalidOperationException("O caminho do projeto não possui diretório.");
            Directory.CreateDirectory(directory);

            var temporaryPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, resolvedPath, overwrite: true);
                _session.CommitSavedSnapshot(project);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            document.HasUnsavedChanges = false;
            Current.Rename(document.DisplayName, resolvedPath, false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private string ResolvePath(string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.Combine(Path.GetDirectoryName(_defaultPath)!, path);

    internal static StudioDocument CreateEmptyDocument()
    {
        var scene = new StudioScene
        {
            Id = Guid.NewGuid().ToString("D"),
            DisplayName = "Cena principal",
            Metadata = "1920 × 1080",
            IsProgram = true
        };
        var document = new StudioDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = "Projeto sem título",
            SelectedSceneId = scene.Id,
            HasUnsavedChanges = true
        };
        document.Scenes.Add(scene);
        return document;
    }
}

public sealed class RuntimeStudioEngineService : IStudioEngineService, IAsyncDisposable
{
    private readonly MediaForgeEngine _engine;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private StudioEngineStatus _status = new(StudioEngineUiState.Stopped, "Pronto");
    private StudioEngineHealth _health = new(StudioEngineUiState.Stopped, "Pronto", DateTimeOffset.UtcNow);
    private int _disposed;

    public RuntimeStudioEngineService(MediaForgeEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _engine.StateChanged += OnEngineStateChanged;
        _engine.RecoveryStateChanged += OnRecoveryStateChanged;
        PublishHealth();
    }

    public StudioEngineStatus CurrentStatus => _status;

    public StudioEngineHealth CurrentHealth => _health;

    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<StudioEngineHealthChangedEventArgs>? HealthChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_engine.State is MediaForgeEngineState.Running or MediaForgeEngineState.Starting)
                return;

            Publish(StudioEngineUiState.Starting, "Preparando composição");
            await _engine.StartAsync(cancellationToken).ConfigureAwait(false);
            Publish(StudioEngineUiState.Running, "Em execução");
            PublishHealth();
        }
        catch (Exception exception)
        {
            Publish(StudioEngineUiState.Failed, $"Falha ao iniciar: {exception.Message}");
            PublishHealth();
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_engine.State is MediaForgeEngineState.Idle or MediaForgeEngineState.Loaded or MediaForgeEngineState.Disposed)
            {
                Publish(StudioEngineUiState.Stopped, "Pronto");
                return;
            }

            Publish(StudioEngineUiState.Stopping, "Finalizando");
            await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            Publish(StudioEngineUiState.Stopped, "Pronto");
            PublishHealth();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _engine.StateChanged -= OnEngineStateChanged;
            _engine.RecoveryStateChanged -= OnRecoveryStateChanged;
            await _engine.DisposeAsync().ConfigureAwait(false);
            Publish(StudioEngineUiState.Stopped, "Encerrado");
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            StatusChanged = null;
            HealthChanged = null;
        }
    }

    private void Publish(StudioEngineUiState state, string message)
    {
        _status = new StudioEngineStatus(state, message);
        StatusChanged?.Invoke(this, new StudioEngineStatusChangedEventArgs(_status));
    }

    private void PublishHealth()
    {
        var snapshot = _engine.GetRuntimeHealthSnapshot();
        var state = snapshot.Status switch
        {
            MediaForgeRuntimeHealthStatus.Healthy => StudioEngineUiState.Running,
            MediaForgeRuntimeHealthStatus.Degraded => StudioEngineUiState.Degraded,
            MediaForgeRuntimeHealthStatus.Recovering => StudioEngineUiState.Recovering,
            MediaForgeRuntimeHealthStatus.Failed => StudioEngineUiState.Failed,
            _ => StudioEngineUiState.Stopped
        };
        var message = snapshot.Recoveries.LastOrDefault()?.Message ?? _status.Message;
        _health = new StudioEngineHealth(state, message, snapshot.CapturedAt);
        HealthChanged?.Invoke(this, new StudioEngineHealthChangedEventArgs(_health));
        if (state is StudioEngineUiState.Degraded or StudioEngineUiState.Recovering or StudioEngineUiState.Failed)
            Publish(state, message);
    }

    private void OnEngineStateChanged(object? sender, MediaForgeEngineStateChangedEventArgs args)
    {
        var state = args.NewState switch
        {
            MediaForgeEngineState.Starting => StudioEngineUiState.Starting,
            MediaForgeEngineState.Running => StudioEngineUiState.Running,
            MediaForgeEngineState.Stopping => StudioEngineUiState.Stopping,
            MediaForgeEngineState.Failed => StudioEngineUiState.Failed,
            _ => StudioEngineUiState.Stopped
        };
        Publish(state, state == StudioEngineUiState.Running ? "Em execução" : state.ToString());
        PublishHealth();
    }

    private void OnRecoveryStateChanged(object? sender, MediaForgeRecoveryEventArgs args) => PublishHealth();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}

public sealed class RuntimeStudioOutputService : IStudioOutputService
{
    public StudioOutputUiState StreamingState { get; private set; } = StudioOutputUiState.NotConfigured;

    public StudioOutputUiState RecordingState { get; private set; } = StudioOutputUiState.NotConfigured;

    public DateTimeOffset? RecordingStartedAt => null;

    public TimeSpan RecordingElapsed => TimeSpan.Zero;

    public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged;

    public Task ToggleStreamingAsync(CancellationToken cancellationToken) => RejectAsync(cancellationToken, streaming: true);

    public Task ToggleRecordingAsync(CancellationToken cancellationToken) => RejectAsync(cancellationToken, streaming: false);

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private Task RejectAsync(CancellationToken cancellationToken, bool streaming)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (streaming)
            StreamingState = StudioOutputUiState.Error;
        else
            RecordingState = StudioOutputUiState.Error;
        StatusChanged?.Invoke(this, new StudioOutputStatusChangedEventArgs(StreamingState, RecordingState));
        return Task.CompletedTask;
    }
}

public sealed class RuntimeStudioCapabilityService : IStudioCapabilityService
{
    private readonly object _gate = new();
    private IReadOnlyList<StudioCapabilityDescriptor> _sources = CreatePendingSources();
    private IReadOnlyList<StudioCapabilityDescriptor> _outputs = CreatePendingOutputs();

    public IReadOnlyList<StudioCapabilityDescriptor> GetSourceCapabilities()
    {
        lock (_gate)
            return _sources;
    }

    public IReadOnlyList<StudioCapabilityDescriptor> GetOutputCapabilities()
    {
        lock (_gate)
            return _outputs;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var snapshot = await MediaForgeWindows.GetCapabilitySnapshotAsync(cancellationToken).ConfigureAwait(false);
        var sources = CreateSourceDescriptors(snapshot.Report);
        var outputs = CreateOutputDescriptors(snapshot.Report);
        lock (_gate)
        {
            _sources = sources;
            _outputs = outputs;
        }
    }

    private static IReadOnlyList<StudioCapabilityDescriptor> CreateSourceDescriptors(MediaForgeCapabilityReport report) =>
    [
        Create(report, $"source.{MediaSourceTypes.Desktop.Value}", "source.desktop", "Tela", "Captura de monitor por GPU", StudioIconKind.Desktop),
        Create(report, $"source.{MediaSourceTypes.Webcam.Value}", "source.webcam", "Webcam", "Captura de câmera local", StudioIconKind.Camera),
        Create(report, $"source.{MediaSourceTypes.VideoFile.Value}", "source.media", "Vídeo", "Arquivo com decode por hardware", StudioIconKind.Video),
        Create(report, $"source.{MediaSourceTypes.ImageFile.Value}", "source.image", "Imagem", "Imagem estática", StudioIconKind.Image),
        Create(report, $"source.{MediaSourceTypes.NdiInput.Value}", "source.ndi", "NDI", "Entrada NDI", StudioIconKind.Stream)
    ];

    private static IReadOnlyList<StudioCapabilityDescriptor> CreateOutputDescriptors(MediaForgeCapabilityReport report) =>
    [
        Create(report, $"output.{RenderOutputTypes.PreviewWindow.Value}", "output.preview", "Prévia local", "Painel de prévia GPU", StudioIconKind.Output),
        Create(report, $"output.{RenderOutputTypes.RecordingMp4.Value}", "output.file.mp4", "Gravação MP4", "H.264 por hardware em MP4", StudioIconKind.Record),
        Create(report, $"output.{RenderOutputTypes.StreamingRtmp.Value}", "output.rtmp", "RTMP", "Transmissão H.264", StudioIconKind.Stream),
        Create(report, $"output.{RenderOutputTypes.Ndi.Value}", "output.ndi", "NDI", "Saída NDI", StudioIconKind.Stream)
    ];

    private static StudioCapabilityDescriptor Create(
        MediaForgeCapabilityReport report,
        string capabilityId,
        string typeId,
        string name,
        string description,
        StudioIconKind icon)
    {
        var entry = report.TryGetEntry(capabilityId);
        var status = entry?.SupportStatus switch
        {
            MediaForgeSupportStatus.Supported => StudioCapabilityStatus.Supported,
            MediaForgeSupportStatus.Experimental => StudioCapabilityStatus.Experimental,
            MediaForgeSupportStatus.Planned => StudioCapabilityStatus.Planned,
            _ => StudioCapabilityStatus.Unavailable
        };
        return new StudioCapabilityDescriptor(typeId, name, description, icon, status,
            entry?.UnavailableReason ?? "Capability não validada nesta máquina.");
    }

    private static IReadOnlyList<StudioCapabilityDescriptor> CreatePendingSources() =>
        [new("source.pending", "Verificando fontes", "Detecção assíncrona de hardware", StudioIconKind.Source, StudioCapabilityStatus.Unavailable, "Aguarde a leitura de capabilities.")];

    private static IReadOnlyList<StudioCapabilityDescriptor> CreatePendingOutputs() =>
        [new("output.pending", "Verificando saídas", "Detecção assíncrona de hardware", StudioIconKind.Output, StudioCapabilityStatus.Unavailable, "Aguarde a leitura de capabilities.")];
}
