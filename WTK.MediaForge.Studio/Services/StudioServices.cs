using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Services;

public sealed class StudioProjectDocument
{
    public StudioProjectDocument(string displayName, string? path = null, bool hasUnsavedChanges = false)
    {
        DisplayName = displayName;
        Path = path;
        HasUnsavedChanges = hasUnsavedChanges;
    }

    public string DisplayName { get; private set; }

    public string? Path { get; private set; }

    public bool HasUnsavedChanges { get; private set; }

    public void Rename(string displayName, string? path, bool hasUnsavedChanges)
    {
        DisplayName = displayName;
        Path = path;
        HasUnsavedChanges = hasUnsavedChanges;
    }
}

public interface IStudioProjectService
{
    StudioProjectDocument Current { get; }

    Task NewAsync(CancellationToken cancellationToken);

    Task OpenAsync(string path, CancellationToken cancellationToken);

    Task SaveAsync(string? path, CancellationToken cancellationToken);
}

public interface IStudioClock
{
    DateTimeOffset Now { get; }
}

public interface IStudioUiTimer
{
    event EventHandler? Tick;

    void Start();

    void Stop();
}

public sealed class StudioEngineStatus
{
    public StudioEngineStatus(StudioEngineUiState state, string message)
    {
        State = state;
        Message = message;
    }

    public StudioEngineUiState State { get; }

    public string Message { get; }
}

public sealed class StudioEngineStatusChangedEventArgs : EventArgs
{
    public StudioEngineStatusChangedEventArgs(StudioEngineStatus status)
    {
        Status = status;
    }

    public StudioEngineStatus Status { get; }
}

public interface IStudioEngineService
{
    StudioEngineStatus CurrentStatus { get; }

    event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record StudioSceneEditRuntimeSession(
    string RuntimeSessionId,
    string StudioSceneId,
    bool IsEngineBacked);

public sealed record StudioSceneEditApplyResult(
    bool IsEngineBacked,
    IReadOnlyList<string> AffectedOutputIds);

public interface IStudioSceneEditRuntimeService
{
    bool IsEngineBacked { get; }

    ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(
        StudioScene scene,
        CancellationToken cancellationToken = default);

    ValueTask TrackLayerVisualStateAsync(
        StudioSceneEditRuntimeSession session,
        StudioLayer layer,
        CancellationToken cancellationToken = default);

    ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioTransition? transition,
        CancellationToken cancellationToken = default);

    ValueTask DiscardSceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        CancellationToken cancellationToken = default);
}

public sealed class StudioOutputStatusChangedEventArgs : EventArgs
{
    public StudioOutputStatusChangedEventArgs(StudioOutputUiState streamingState, StudioOutputUiState recordingState)
    {
        StreamingState = streamingState;
        RecordingState = recordingState;
    }

    public StudioOutputUiState StreamingState { get; }

    public StudioOutputUiState RecordingState { get; }
}

public interface IStudioOutputService
{
    StudioOutputUiState StreamingState { get; }

    StudioOutputUiState RecordingState { get; }

    DateTimeOffset? RecordingStartedAt { get; }

    TimeSpan RecordingElapsed { get; }

    event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged;

    Task ToggleStreamingAsync(CancellationToken cancellationToken);

    Task ToggleRecordingAsync(CancellationToken cancellationToken);

    Task StopAllAsync(CancellationToken cancellationToken);
}

public enum StudioCapabilityStatus
{
    Supported,
    Experimental,
    Unavailable,
    Planned,
    Blocked
}

public sealed record StudioCapabilityDescriptor(
    string TypeId,
    string DisplayName,
    string Description,
    StudioIconKind IconKind,
    StudioCapabilityStatus Status,
    string Reason = "")
{
    public bool IsSelectable =>
        Status is StudioCapabilityStatus.Supported or StudioCapabilityStatus.Experimental;

    public string Badge => Status switch
    {
        StudioCapabilityStatus.Supported => "Suportado",
        StudioCapabilityStatus.Experimental => "Experimental",
        StudioCapabilityStatus.Unavailable => "Indisponível",
        StudioCapabilityStatus.Planned => "Planejado",
        StudioCapabilityStatus.Blocked => "Bloqueado",
        _ => string.Empty
    };

    public string DialogDescription =>
        string.IsNullOrWhiteSpace(Reason)
            ? Description
            : $"{Description}. {Badge}: {Reason}";
}

public interface IStudioCapabilityService
{
    IReadOnlyList<StudioCapabilityDescriptor> GetSourceCapabilities();

    IReadOnlyList<StudioCapabilityDescriptor> GetOutputCapabilities();
}

public interface IStudioDiagnosticsService
{
    ObservableCollection<DiagnosticLogItemViewModel> Items { get; }

    void Append(string level, string category, string message);
}

public sealed class StudioSelectionChangedEventArgs : EventArgs
{
    public StudioSelectionChangedEventArgs(StudioSelectionState selection)
    {
        Selection = selection;
    }

    public StudioSelectionState Selection { get; }
}

public interface IStudioSelectionService
{
    StudioSelectionState Current { get; }

    event EventHandler<StudioSelectionChangedEventArgs>? SelectionChanged;

    void Select(StudioSelectionState selection);
}

public interface IInspectorPageFactory
{
    InspectorPageViewModel Create(StudioSelectionState selection, ICommand? reconnectCommand);
}

public sealed class StudioServiceBundle
{
    public StudioServiceBundle(
        IStudioProjectService projectService,
        IStudioEngineService engineService,
        IStudioSceneEditRuntimeService sceneEditRuntimeService,
        IStudioOutputService outputService,
        IStudioCapabilityService capabilityService,
        IStudioDiagnosticsService diagnosticsService,
        IStudioSelectionService selectionService,
        IInspectorPageFactory inspectorPageFactory,
        IStudioUiTimer uiTimer)
    {
        ProjectService = projectService;
        EngineService = engineService;
        SceneEditRuntimeService = sceneEditRuntimeService;
        OutputService = outputService;
        CapabilityService = capabilityService;
        DiagnosticsService = diagnosticsService;
        SelectionService = selectionService;
        InspectorPageFactory = inspectorPageFactory;
        UiTimer = uiTimer;
    }

    public IStudioProjectService ProjectService { get; }

    public IStudioEngineService EngineService { get; }

    public IStudioSceneEditRuntimeService SceneEditRuntimeService { get; }

    public IStudioOutputService OutputService { get; }

    public IStudioCapabilityService CapabilityService { get; }

    public IStudioDiagnosticsService DiagnosticsService { get; }

    public IStudioSelectionService SelectionService { get; }

    public IInspectorPageFactory InspectorPageFactory { get; }

    public IStudioUiTimer UiTimer { get; }
}
