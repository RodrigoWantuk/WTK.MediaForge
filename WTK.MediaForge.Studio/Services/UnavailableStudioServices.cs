using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Services;

internal sealed class UnavailableStudioEngineService(string reason) : IStudioEngineService
{
    private readonly StudioEngineStatus _status = new(StudioEngineUiState.Failed, reason);
    private readonly StudioEngineHealth _health = new(StudioEngineUiState.Failed, reason, DateTimeOffset.UtcNow);

    public StudioEngineStatus CurrentStatus => _status;
    public StudioEngineHealth CurrentHealth => _health;
    public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged { add { } remove { } }
    public event EventHandler<StudioEngineHealthChangedEventArgs>? HealthChanged { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken) => Task.FromException(new NotSupportedException(reason));
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class UnavailableStudioSceneEditRuntimeService(string reason) : IStudioSceneEditRuntimeService
{
    public bool IsEngineBacked => false;
    public ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(StudioDocument document, StudioScene scene, CancellationToken cancellationToken = default) => Unavailable<StudioSceneEditRuntimeSession>();
    public ValueTask TrackLayerVisualStateAsync(StudioSceneEditRuntimeSession session, StudioLayer layer, CancellationToken cancellationToken = default) => Unavailable();
    public ValueTask TrackSceneDraftAsync(StudioSceneEditRuntimeSession session, StudioDocument document, StudioScene originalScene, StudioScene draftScene, CancellationToken cancellationToken = default) => Unavailable();
    public ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(StudioSceneEditRuntimeSession session, StudioTransition? transition, CancellationToken cancellationToken = default) => Unavailable<StudioSceneEditApplyResult>();
    public ValueTask DiscardSceneDraftAsync(StudioSceneEditRuntimeSession session, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private ValueTask Unavailable() => ValueTask.FromException(new NotSupportedException(reason));
    private ValueTask<T> Unavailable<T>() => ValueTask.FromException<T>(new NotSupportedException(reason));
}

internal sealed class UnavailableStudioOutputService(string reason) : IStudioOutputService
{
    public StudioOutputUiState StreamingState => StudioOutputUiState.NotConfigured;
    public StudioOutputUiState RecordingState => StudioOutputUiState.NotConfigured;
    public DateTimeOffset? RecordingStartedAt => null;
    public TimeSpan RecordingElapsed => TimeSpan.Zero;
    public bool CanToggleStreaming => false;
    public bool CanToggleRecording => false;
    public string? StreamingDetail => reason;
    public string? RecordingDetail => reason;
    public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged { add { } remove { } }
    public Task ToggleStreamingAsync(CancellationToken cancellationToken) => Task.FromException(new NotSupportedException(reason));
    public Task ToggleRecordingAsync(CancellationToken cancellationToken) => Task.FromException(new NotSupportedException(reason));
    public Task StopAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public StudioOutputStatus GetOutputStatus(RenderOutputId outputId) => new(outputId, StudioOutputUiState.NotConfigured, reason, null, TimeSpan.Zero);
}
