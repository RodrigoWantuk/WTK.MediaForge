using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.Coordinators;

internal sealed class StudioProjectCoordinator(IStudioProjectService inner) : IStudioProjectService
{
    public StudioProjectDocument Current => inner.Current;
    public Task<StudioDocument> NewAsync(CancellationToken token) => inner.NewAsync(token);
    public Task<StudioDocument> OpenAsync(string path, CancellationToken token) => inner.OpenAsync(path, token);
    public Task SaveAsync(StudioDocument document, string? path, CancellationToken token) => inner.SaveAsync(document, path, token);
}

internal sealed class StudioEngineLifecycleCoordinator(IStudioEngineService inner) : IStudioEngineService
{
    public StudioEngineStatus CurrentStatus => inner.CurrentStatus;
    public StudioEngineHealth CurrentHealth => inner.CurrentHealth;
    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged
    {
        add => inner.StatusChanged += value;
        remove => inner.StatusChanged -= value;
    }
    public event EventHandler<StudioEngineHealthChangedEventArgs>? HealthChanged
    {
        add => inner.HealthChanged += value;
        remove => inner.HealthChanged -= value;
    }
    public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
    public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
}

internal sealed class StudioOutputCoordinator(IStudioOutputService inner) : IStudioOutputService
{
    public StudioOutputUiState StreamingState => inner.StreamingState;
    public StudioOutputUiState RecordingState => inner.RecordingState;
    public DateTimeOffset? RecordingStartedAt => inner.RecordingStartedAt;
    public TimeSpan RecordingElapsed => inner.RecordingElapsed;
    public bool CanToggleStreaming => inner.CanToggleStreaming;
    public bool CanToggleRecording => inner.CanToggleRecording;
    public string? StreamingDetail => inner.StreamingDetail;
    public string? RecordingDetail => inner.RecordingDetail;
    public StudioOutputMetrics? StreamingMetrics => inner.StreamingMetrics;
    public StudioOutputMetrics? RecordingMetrics => inner.RecordingMetrics;
    public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged
    {
        add => inner.StatusChanged += value;
        remove => inner.StatusChanged -= value;
    }
    public Task ToggleStreamingAsync(CancellationToken token) => inner.ToggleStreamingAsync(token);
    public Task ToggleRecordingAsync(CancellationToken token) => inner.ToggleRecordingAsync(token);
    public Task StopAllAsync(CancellationToken token) => inner.StopAllAsync(token);
    public void RefreshStatus() => inner.RefreshStatus();
}

internal sealed class StudioSelectionCoordinator(IStudioSelectionService inner) : IStudioSelectionService
{
    public StudioSelectionState Current => inner.Current;
    public event EventHandler<StudioSelectionChangedEventArgs>? SelectionChanged
    {
        add => inner.SelectionChanged += value;
        remove => inner.SelectionChanged -= value;
    }
    public void Select(StudioSelectionState selection) => inner.Select(selection);
}

internal sealed class StudioLayoutCoordinator(IStudioLayoutService inner) : IStudioLayoutService
{
    public StudioLayoutDocument Load() => inner.Load();
    public void Save(StudioLayoutDocument document) => inner.Save(document);
}

internal sealed class StudioSceneEditCoordinator(IStudioSceneEditRuntimeService inner) : IStudioSceneEditRuntimeService
{
    public bool IsEngineBacked => inner.IsEngineBacked;
    public ValueTask SynchronizeProjectAsync(StudioDocument document, CancellationToken token = default) => inner.SynchronizeProjectAsync(document, token);
    public ValueTask TransitionOutputToSceneAsync(string outputId, string destinationSceneId, StudioTransition transition, CancellationToken token = default) => inner.TransitionOutputToSceneAsync(outputId, destinationSceneId, transition, token);
    public ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(StudioDocument document, StudioScene scene, CancellationToken token = default) => inner.BeginApplySessionAsync(document, scene, token);
    public ValueTask<StudioSceneEditRuntimeSession> BeginLiveSessionAsync(StudioDocument document, StudioScene scene, CancellationToken token = default) => inner.BeginLiveSessionAsync(document, scene, token);
    public ValueTask TrackLayerVisualStateAsync(StudioSceneEditRuntimeSession session, StudioLayer layer, CancellationToken token = default) => inner.TrackLayerVisualStateAsync(session, layer, token);
    public ValueTask TrackSceneDraftAsync(StudioSceneEditRuntimeSession session, StudioDocument document, StudioScene original, StudioScene draft, CancellationToken token = default) => inner.TrackSceneDraftAsync(session, document, original, draft, token);
    public ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(StudioSceneEditRuntimeSession session, StudioTransition? transition, CancellationToken token = default) => inner.ApplySceneDraftAsync(session, transition, token);
    public ValueTask DiscardSceneDraftAsync(StudioSceneEditRuntimeSession session, CancellationToken token = default) => inner.DiscardSceneDraftAsync(session, token);
    public ValueTask DiscardAllSceneDraftsAsync(CancellationToken token = default) => inner.DiscardAllSceneDraftsAsync(token);
    public ValueTask FlushLiveMutationsAsync(StudioSceneEditRuntimeSession session, CancellationToken token = default) => inner.FlushLiveMutationsAsync(session, token);
    public string? GetLastMutationError(StudioSceneEditRuntimeSession session) => inner.GetLastMutationError(session);
}
