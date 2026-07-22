using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Identifiers;
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

internal interface IStudioEncodedOutputEngine
{
    MediaForgeEngineState State { get; }
    MediaForgeProject? CurrentProject { get; }
    IReadOnlyList<EncodedOutputRuntimeSnapshot> GetSnapshots();
    Task StartAsync(RenderOutputId outputId, CancellationToken cancellationToken);
    Task StopAsync(RenderOutputId outputId, CancellationToken cancellationToken);
    Task SetRecordingPathAsync(
        RenderOutputId outputId,
        RecordingMp4OutputSettings settings,
        string path,
        CancellationToken cancellationToken);
}

internal sealed class StudioEncodedOutputEngine(MediaForgeEngine engine) : IStudioEncodedOutputEngine
{
    private readonly MediaForgeEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    public MediaForgeEngineState State => _engine.State;
    public MediaForgeProject? CurrentProject => _engine.CurrentProject;
    public IReadOnlyList<EncodedOutputRuntimeSnapshot> GetSnapshots() => _engine.GetEncodedOutputRuntimeSnapshots();
    public Task StartAsync(RenderOutputId outputId, CancellationToken cancellationToken) =>
        _engine.StartEncodedOutputAsync(outputId, cancellationToken);
    public Task StopAsync(RenderOutputId outputId, CancellationToken cancellationToken) =>
        _engine.StopEncodedOutputAsync(outputId, cancellationToken);
    public Task SetRecordingPathAsync(
        RenderOutputId outputId,
        RecordingMp4OutputSettings settings,
        string path,
        CancellationToken cancellationToken) =>
        _engine.ApplyProjectUpdateAsync(
            editor => editor.Project.Outputs.Single(candidate => candidate.Id == outputId).Settings =
                RenderOutputSettingsSerializer.ToJson(new RecordingMp4OutputSettings
                {
                    Path = path,
                    Video = settings.Video,
                    SchemaVersion = settings.SchemaVersion
                }),
            cancellationToken);
}

public sealed class RuntimeStudioOutputService : IStudioOutputService
{
    private readonly IStudioEncodedOutputEngine _engine;
    private readonly IStudioCapabilityService _capabilities;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<RenderOutputId, RecordingOutputSession> _recordingSessions = [];
    private DateTimeOffset? _recordingStartedAt;

    public RuntimeStudioOutputService(MediaForgeEngine engine, IStudioCapabilityService capabilities)
        : this(new StudioEncodedOutputEngine(engine), capabilities)
    {
    }

    internal RuntimeStudioOutputService(IStudioEncodedOutputEngine engine, IStudioCapabilityService capabilities)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public StudioOutputUiState StreamingState { get; private set; } = StudioOutputUiState.NotConfigured;

    public StudioOutputUiState RecordingState { get; private set; } = StudioOutputUiState.NotConfigured;

    public DateTimeOffset? RecordingStartedAt => _recordingStartedAt;

    public TimeSpan RecordingElapsed => RecordingState == StudioOutputUiState.Running && _recordingStartedAt is not null
        ? DateTimeOffset.UtcNow - _recordingStartedAt.Value
        : TimeSpan.Zero;

    public bool CanToggleStreaming => StreamingState == StudioOutputUiState.Running ||
        CanStart(RenderOutputTypes.StreamingRtmp, "output.rtmp");

    public bool CanToggleRecording => RecordingState == StudioOutputUiState.Running ||
        CanStart(RenderOutputTypes.RecordingMp4, "output.file.mp4");

    public string? StreamingDetail { get; private set; }

    public string? RecordingDetail { get; private set; }

    public StudioOutputMetrics? StreamingMetrics { get; private set; }

    public StudioOutputMetrics? RecordingMetrics { get; private set; }

    public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged;

    public Task ToggleStreamingAsync(CancellationToken cancellationToken) =>
        ToggleGroupAsync(RenderOutputTypes.StreamingRtmp, streaming: true, cancellationToken);

    public Task ToggleRecordingAsync(CancellationToken cancellationToken) =>
        ToggleGroupAsync(RenderOutputTypes.RecordingMp4, streaming: false, cancellationToken);

    private async Task ToggleGroupAsync(
        RenderOutputTypeId typeId,
        bool streaming,
        CancellationToken cancellationToken)
    {
        var outputs = _engine.CurrentProject?.Outputs.Where(output => output.TypeId == typeId).ToArray() ?? [];
        if (outputs.Length == 0)
        {
            SetState(streaming, StudioOutputUiState.NotConfigured, "Nenhuma rota configurada.");
            Publish();
            return;
        }

        var runningIds = _engine.GetSnapshots().Select(snapshot => snapshot.OutputId).ToHashSet();
        var stop = outputs.Any(output => runningIds.Contains(output.Id));
        if (!stop && !(streaming ? CanToggleStreaming : CanToggleRecording))
        {
            SetState(
                streaming,
                StudioOutputUiState.NotConfigured,
                CapabilityReason(streaming ? "output.rtmp" : "output.file.mp4"));
            Publish();
            return;
        }
        List<Exception>? failures = null;
        foreach (var output in outputs)
        {
            if (stop != runningIds.Contains(output.Id))
                continue;
            try
            {
                if (stop)
                    await StopOutputAsync(output.Id, cancellationToken).ConfigureAwait(false);
                else
                    await StartOutputAsync(output.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        RefreshStatus();
        if (failures is not null)
        {
            SetState(streaming, StudioOutputUiState.Error, string.Join(" | ", failures.Select(static failure => failure.Message)));
            Publish();
        }
    }

    public async Task StartOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var output = RequireOutput(outputId);
            if (_engine.GetSnapshots().Any(snapshot => snapshot.OutputId == outputId))
                return;
            EnsureCanStart(output);
            if (output.TypeId == RenderOutputTypes.RecordingMp4)
                await PrepareRecordingSegmentAsync(outputId, cancellationToken).ConfigureAwait(false);
            await _engine.StartAsync(outputId, cancellationToken).ConfigureAwait(false);
            if (output.TypeId == RenderOutputTypes.RecordingMp4)
                MarkRecordingStarted(outputId);
            RefreshStatus();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var output = RequireOutput(outputId);
            if (_engine.GetSnapshots().Any(snapshot => snapshot.OutputId == outputId))
                await _engine.StopAsync(outputId, cancellationToken).ConfigureAwait(false);
            if (output.TypeId == RenderOutputTypes.RecordingMp4 && _recordingSessions.TryGetValue(outputId, out var session))
                session.StartedAt = null;
            RefreshStatus();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RestartOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var output = RequireOutput(outputId);
            EnsureCanStart(output);
            if (_engine.GetSnapshots().Any(snapshot => snapshot.OutputId == outputId))
                await _engine.StopAsync(outputId, cancellationToken).ConfigureAwait(false);
            if (output.TypeId == RenderOutputTypes.RecordingMp4)
                await PrepareRecordingSegmentAsync(outputId, cancellationToken).ConfigureAwait(false);
            await _engine.StartAsync(outputId, cancellationToken).ConfigureAwait(false);
            if (output.TypeId == RenderOutputTypes.RecordingMp4)
                MarkRecordingStarted(outputId);
            RefreshStatus();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public StudioOutputStatus GetOutputStatus(RenderOutputId outputId)
    {
        var output = _engine.CurrentProject?.Outputs.FirstOrDefault(candidate => candidate.Id == outputId);
        if (output is null)
            return new StudioOutputStatus(outputId, StudioOutputUiState.NotConfigured, "Output was not found.", null, TimeSpan.Zero);
        var snapshot = _engine.GetSnapshots().FirstOrDefault(candidate => candidate.OutputId == outputId);
        var state = snapshot is null
            ? (CanStart(output.TypeId, CapabilityTypeId(output.TypeId)) ? StudioOutputUiState.Ready : StudioOutputUiState.NotConfigured)
            : ToStudioState(snapshot.Status);
        var startedAt = output.TypeId == RenderOutputTypes.RecordingMp4 &&
            _recordingSessions.TryGetValue(outputId, out var session)
                ? session.StartedAt
                : null;
        return new StudioOutputStatus(
            outputId,
            state,
            snapshot?.Reason ?? (state == StudioOutputUiState.NotConfigured ? CapabilityReason(CapabilityTypeId(output.TypeId)) : null),
            startedAt,
            state == StudioOutputUiState.Running && startedAt is not null
                ? DateTimeOffset.UtcNow - startedAt.Value
                : TimeSpan.Zero);
    }

    public StudioOutputMetrics? GetMetrics(RenderOutputId outputId)
    {
        var snapshot = _engine.GetSnapshots().FirstOrDefault(candidate => candidate.OutputId == outputId);
        return snapshot is null
            ? null
            : new StudioOutputMetrics(
                snapshot.FramesSubmitted,
                snapshot.PacketsProduced,
                snapshot.PacketsWritten,
                snapshot.FramesDropped,
                snapshot.LastPacketLatency);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Exception>? failures = null;
        foreach (var output in _engine.CurrentProject?.Outputs.Where(output =>
                     output.TypeId == RenderOutputTypes.StreamingRtmp ||
                     output.TypeId == RenderOutputTypes.RecordingMp4).ToArray() ?? [])
        {
            if (!_engine.GetSnapshots().Any(snapshot => snapshot.OutputId == output.Id))
                continue;
            try
            {
                await _engine.StopAsync(output.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        _recordingStartedAt = null;
        foreach (var session in _recordingSessions.Values)
            session.StartedAt = null;
        RefreshStatus();
        if (failures is not null)
            throw new AggregateException("One or more Studio outputs failed to stop.", failures);
    }

    public void RefreshStatus()
    {
        var snapshots = _engine.GetSnapshots();
        ApplySnapshot(
            streaming: true,
            FindOutput(RenderOutputTypes.StreamingRtmp),
            snapshots);
        ApplySnapshot(
            streaming: false,
            FindOutput(RenderOutputTypes.RecordingMp4),
            snapshots);
        Publish();
    }

    private async Task PrepareRecordingSegmentAsync(RenderOutputId outputId, CancellationToken cancellationToken)
    {
        var output = _engine.CurrentProject!.Outputs.Single(candidate => candidate.Id == outputId);
        var settings = (RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(output.TypeId, output.Settings);
        if (!_recordingSessions.TryGetValue(outputId, out var session))
        {
            session = new RecordingOutputSession(settings.Path);
            _recordingSessions.Add(outputId, session);
        }
        if (session.Segment == 0)
            return;
        var directory = Path.GetDirectoryName(session.BasePath);
        var name = Path.GetFileNameWithoutExtension(session.BasePath);
        var extension = Path.GetExtension(session.BasePath);
        var segmentPath = Path.Combine(directory ?? string.Empty, $"{name}.segment-{session.Segment + 1:0000}{extension}");
        await _engine.SetRecordingPathAsync(outputId, settings, segmentPath, cancellationToken).ConfigureAwait(false);
    }

    private void ApplySnapshot(
        bool streaming,
        MediaForgeRenderOutput? output,
        IReadOnlyList<EncodedOutputRuntimeSnapshot> snapshots)
    {
        if (output is null)
        {
            SetMetrics(streaming, null);
            SetState(streaming, StudioOutputUiState.NotConfigured, "Nenhuma rota configurada.");
            return;
        }
        var snapshot = snapshots.FirstOrDefault(candidate => candidate.OutputId == output.Id);
        if (snapshot is null)
        {
            SetMetrics(streaming, null);
            var typeId = streaming ? "output.rtmp" : "output.file.mp4";
            SetState(streaming, IsCapabilitySelectable(typeId) ? StudioOutputUiState.Ready : StudioOutputUiState.NotConfigured, CapabilityReason(typeId));
            return;
        }
        var state = ToStudioState(snapshot.Status);
        SetMetrics(streaming, new StudioOutputMetrics(
            snapshot.FramesSubmitted,
            snapshot.PacketsProduced,
            snapshot.PacketsWritten,
            snapshot.FramesDropped,
            snapshot.LastPacketLatency));
        if (!streaming && state == StudioOutputUiState.Running && _recordingStartedAt is null)
        {
            _recordingStartedAt = DateTimeOffset.UtcNow;
            if (!_recordingSessions.TryGetValue(output.Id, out var session))
            {
                var settings = (RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(output.TypeId, output.Settings);
                session = new RecordingOutputSession(settings.Path);
                _recordingSessions.Add(output.Id, session);
            }
            session.Segment = Math.Max(1, session.Segment);
            session.StartedAt ??= _recordingStartedAt;
        }
        SetState(streaming, state, snapshot.Reason);
    }

    private bool CanStart(RenderOutputTypeId outputType, string capabilityTypeId) =>
        _engine.State == MediaForgeEngineState.Running &&
        FindOutput(outputType) is not null &&
        IsCapabilitySelectable(capabilityTypeId);

    private MediaForgeRenderOutput? FindOutput(RenderOutputTypeId typeId) =>
        _engine.CurrentProject?.Outputs.FirstOrDefault(output => output.TypeId == typeId);

    private MediaForgeRenderOutput RequireOutput(RenderOutputId outputId) =>
        _engine.CurrentProject?.Outputs.FirstOrDefault(output => output.Id == outputId)
        ?? throw new InvalidOperationException($"Output '{outputId}' was not found in the current project.");

    private void EnsureCanStart(MediaForgeRenderOutput output)
    {
        var capabilityTypeId = CapabilityTypeId(output.TypeId);
        if (!CanStart(output.TypeId, capabilityTypeId))
            throw new InvalidOperationException(CapabilityReason(capabilityTypeId) ?? $"Output '{output.Id}' is unavailable.");
    }

    private static string CapabilityTypeId(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.StreamingRtmp ? "output.rtmp" :
        typeId == RenderOutputTypes.RecordingMp4 ? "output.file.mp4" :
        $"output.{typeId.Value}";

    private static StudioOutputUiState ToStudioState(EncodedOutputRuntimeStatus status) => status switch
    {
        EncodedOutputRuntimeStatus.Starting => StudioOutputUiState.Starting,
        EncodedOutputRuntimeStatus.Running or EncodedOutputRuntimeStatus.Backpressure => StudioOutputUiState.Running,
        EncodedOutputRuntimeStatus.Failed => StudioOutputUiState.Error,
        EncodedOutputRuntimeStatus.Unavailable => StudioOutputUiState.NotConfigured,
        _ => StudioOutputUiState.Ready
    };

    private void MarkRecordingStarted(RenderOutputId outputId)
    {
        if (!_recordingSessions.TryGetValue(outputId, out var session))
            throw new InvalidOperationException($"Recording output '{outputId}' has no segment state.");
        session.Segment++;
        session.StartedAt = DateTimeOffset.UtcNow;
        _recordingStartedAt = session.StartedAt;
    }

    private bool IsCapabilitySelectable(string typeId) =>
        _capabilities.GetOutputCapabilities().FirstOrDefault(capability => capability.TypeId == typeId)?.IsSelectable == true;

    private string? CapabilityReason(string typeId) =>
        _capabilities.GetOutputCapabilities().FirstOrDefault(capability => capability.TypeId == typeId)?.Reason;

    private void SetState(bool streaming, StudioOutputUiState state, string? detail)
    {
        if (streaming)
        {
            StreamingState = state;
            StreamingDetail = detail;
        }
        else
        {
            RecordingState = state;
            RecordingDetail = detail;
        }
    }

    private void SetMetrics(bool streaming, StudioOutputMetrics? metrics)
    {
        if (streaming)
            StreamingMetrics = metrics;
        else
            RecordingMetrics = metrics;
    }

    private void Publish() => StatusChanged?.Invoke(
        this,
        new StudioOutputStatusChangedEventArgs(StreamingState, RecordingState, StreamingDetail, RecordingDetail));

    private sealed class RecordingOutputSession(string basePath)
    {
        public string BasePath { get; } = basePath;
        public int Segment { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
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
