using System.Collections.ObjectModel;
using System.Windows.Input;
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
        IStudioOutputService outputService,
        IStudioDiagnosticsService diagnosticsService,
        IStudioSelectionService selectionService,
        IInspectorPageFactory inspectorPageFactory,
        IStudioUiTimer uiTimer)
    {
        ProjectService = projectService;
        EngineService = engineService;
        OutputService = outputService;
        DiagnosticsService = diagnosticsService;
        SelectionService = selectionService;
        InspectorPageFactory = inspectorPageFactory;
        UiTimer = uiTimer;
    }

    public IStudioProjectService ProjectService { get; }

    public IStudioEngineService EngineService { get; }

    public IStudioOutputService OutputService { get; }

    public IStudioDiagnosticsService DiagnosticsService { get; }

    public IStudioSelectionService SelectionService { get; }

    public IInspectorPageFactory InspectorPageFactory { get; }

    public IStudioUiTimer UiTimer { get; }
}
