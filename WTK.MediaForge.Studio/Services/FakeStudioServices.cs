using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Services;

public sealed class FakeStudioProjectService : IStudioProjectService
{
    public StudioProjectDocument Current { get; } = new("Live Production Workspace");

    public Task NewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename("Untitled MediaForge Project", null, true);
        return Task.CompletedTask;
    }

    public Task OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename("Loaded MediaForge Project", path, false);
        return Task.CompletedTask;
    }

    public Task SaveAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current.Rename(Current.DisplayName, path ?? Current.Path ?? "mock-project.mforge.json", false);
        return Task.CompletedTask;
    }
}

public sealed class FakeStudioEngineService : IStudioEngineService
{
    private StudioEngineStatus _currentStatus = new(StudioEngineUiState.Stopped, "Engine stopped");

    public StudioEngineStatus CurrentStatus => _currentStatus;

    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_currentStatus.State is StudioEngineUiState.Running or StudioEngineUiState.Starting)
        {
            return;
        }

        Publish(new StudioEngineStatus(StudioEngineUiState.Starting, "Starting mock engine"));
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        Publish(new StudioEngineStatus(StudioEngineUiState.Running, "Mock engine running"));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_currentStatus.State is StudioEngineUiState.Stopped or StudioEngineUiState.Stopping)
        {
            return;
        }

        Publish(new StudioEngineStatus(StudioEngineUiState.Stopping, "Stopping mock engine"));
        await Task.Delay(5, cancellationToken).ConfigureAwait(true);
        Publish(new StudioEngineStatus(StudioEngineUiState.Stopped, "Engine stopped"));
    }

    private void Publish(StudioEngineStatus status)
    {
        _currentStatus = status;
        StatusChanged?.Invoke(this, new StudioEngineStatusChangedEventArgs(status));
    }
}

public sealed class FakeStudioOutputService : IStudioOutputService
{
    public StudioOutputUiState StreamingState { get; private set; } = StudioOutputUiState.Ready;

    public StudioOutputUiState RecordingState { get; private set; } = StudioOutputUiState.Ready;

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
        RecordingState = RecordingState == StudioOutputUiState.Stopping ? StudioOutputUiState.Ready : StudioOutputUiState.Running;
        Publish();
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StreamingState = StudioOutputUiState.Ready;
        RecordingState = StudioOutputUiState.Ready;
        Publish();
        return Task.CompletedTask;
    }

    private void Publish()
    {
        StatusChanged?.Invoke(this, new StudioOutputStatusChangedEventArgs(StreamingState, RecordingState));
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
        return selection.Kind switch
        {
            StudioSelectionKind.Scene => new SceneInspectorViewModel(selection.DisplayName, selection.Detail),
            StudioSelectionKind.Source => new SourceInspectorViewModel(selection.DisplayName, selection.TypeId, selection.Detail)
            {
                ReconnectCommand = reconnectCommand
            },
            StudioSelectionKind.Layer => new LayerInspectorViewModel(selection.DisplayName, selection.Detail),
            StudioSelectionKind.Output => new OutputInspectorViewModel(
                selection.DisplayName,
                selection.Destination,
                selection.Codec,
                selection.Bitrate,
                selection.Secret),
            StudioSelectionKind.Preset => new PresetInspectorViewModel(selection.DisplayName, selection.Metadata),
            StudioSelectionKind.Package => new PackageInspectorViewModel(selection.DisplayName, selection.Metadata),
            _ => new EmptyInspectorViewModel()
        };
    }
}

public static class StudioServiceFactory
{
    public static StudioServiceBundle CreateFake(IEnumerable<DiagnosticLogItemViewModel>? diagnostics = null)
    {
        return new StudioServiceBundle(
            new FakeStudioProjectService(),
            new FakeStudioEngineService(),
            new FakeStudioOutputService(),
            new StudioDiagnosticsService(diagnostics),
            new StudioSelectionService(),
            new StudioInspectorPageFactory());
    }
}
