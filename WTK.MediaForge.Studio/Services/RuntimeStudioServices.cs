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

    public Task<StudioDocument> NewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename("Projeto sem título", _defaultPath, true);
        return Task.FromResult(CreateEmptyDocument());
    }

    public async Task<StudioDocument> OpenAsync(string path, CancellationToken cancellationToken)
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
        var document = _mapper.CreateDocument(result.Project, displayName);
        Current.Rename(document.DisplayName, resolvedPath, false);
        return document;
    }

    public async Task SaveAsync(StudioDocument document, string? path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var resolvedPath = ResolvePath(path ?? Current.Path ?? _defaultPath);
        var project = _mapper.CreateProject(document);
        var json = MediaForgeProjectSerializer.Serialize(project);
        var directory = Path.GetDirectoryName(resolvedPath)
            ?? throw new InvalidOperationException("O caminho do projeto não possui diretório.");
        Directory.CreateDirectory(directory);

        var temporaryPath = resolvedPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, resolvedPath, overwrite: true);

        document.HasUnsavedChanges = false;
        Current.Rename(document.DisplayName, resolvedPath, false);
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

public sealed class RuntimeStudioEngineService(MediaForgeEngine engine) : IStudioEngineService, IAsyncDisposable
{
    private readonly MediaForgeEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private StudioEngineStatus _status = new(StudioEngineUiState.Stopped, "Pronto");

    public StudioEngineStatus CurrentStatus => _status;

    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Publish(StudioEngineUiState.Starting, "Preparando composição");
        try
        {
            await _engine.StartAsync(cancellationToken).ConfigureAwait(false);
            Publish(StudioEngineUiState.Running, "Em execução");
        }
        catch
        {
            Publish(StudioEngineUiState.Failed, "Falha ao iniciar");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Publish(StudioEngineUiState.Stopping, "Finalizando");
        await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
        Publish(StudioEngineUiState.Stopped, "Pronto");
    }

    public ValueTask DisposeAsync() => _engine.DisposeAsync();

    private void Publish(StudioEngineUiState state, string message)
    {
        _status = new StudioEngineStatus(state, message);
        StatusChanged?.Invoke(this, new StudioEngineStatusChangedEventArgs(_status));
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
