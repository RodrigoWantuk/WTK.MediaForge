using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioEngineLifecycleTests
{
    [Fact]
    public async Task Repeated_start_stop_is_idempotent()
    {
        var engine = new RecordingEngineService();
        var shell = CreateShell(engine);

        await shell.InitializeAsync();
        await shell.InitializeAsync();
        await shell.StopEngineCommand.ExecuteAsync(null);
        await shell.StopEngineCommand.ExecuteAsync(null);

        Assert.Equal(1, engine.StartCount);
        Assert.Equal(1, engine.StopCount);
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task Start_failure_is_reflected_and_actions_remain_safe()
    {
        var engine = new RecordingEngineService { FailStart = true };
        var shell = CreateShell(engine);

        await shell.StartEngineCommand.ExecuteAsync(null);

        Assert.Equal(StudioEngineUiState.Failed, shell.EngineStatus.State);
        Assert.True(shell.StartEngineCommand.CanExecute(null));
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task Project_replacement_discards_all_runtime_drafts_before_loading()
    {
        var engine = new RecordingEngineService();
        var drafts = new RecordingSceneRuntimeService();
        var shell = CreateShell(engine, drafts: drafts);
        await shell.InitializeAsync();

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Equal(1, drafts.DiscardAllCount);
        Assert.Equal(1, engine.StopCount);
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_waits_for_active_output_shutdown_and_is_idempotent()
    {
        var engine = new RecordingEngineService();
        var outputs = new RecordingOutputService { HoldStop = true };
        var shell = CreateShell(engine, outputs: outputs);
        await shell.InitializeAsync();

        var dispose = shell.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        outputs.ReleaseStop();
        await dispose;
        await shell.DisposeAsync();

        Assert.Equal(1, outputs.StopAllCount);
        Assert.Equal(1, engine.StopCount);
    }

    [Fact]
    public async Task Dispose_detaches_engine_status_handlers()
    {
        var engine = new RecordingEngineService();
        var shell = CreateShell(engine);
        await shell.DisposeAsync();
        var status = shell.EngineStatus;

        engine.Publish(StudioEngineUiState.Failed, "late callback");

        Assert.Same(status, shell.EngineStatus);
    }

    private static StudioShellViewModel CreateShell(
        RecordingEngineService engine,
        RecordingSceneRuntimeService? drafts = null,
        RecordingOutputService? outputs = null)
    {
        var services = StudioServiceFactory.CreateFake(
            engineService: engine,
            sceneEditRuntimeService: drafts ?? new RecordingSceneRuntimeService(),
            outputService: outputs ?? new RecordingOutputService(),
            uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }

    private sealed class RecordingEngineService : IStudioEngineService
    {
        public StudioEngineStatus CurrentStatus { get; private set; } = new(StudioEngineUiState.Stopped, "Pronto");
        public StudioEngineHealth CurrentHealth { get; private set; } = new(StudioEngineUiState.Stopped, "Pronto", DateTimeOffset.UtcNow);
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool FailStart { get; init; }
        public event EventHandler<StudioEngineStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<StudioEngineHealthChangedEventArgs>? HealthChanged;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentStatus.State == StudioEngineUiState.Running)
                return Task.CompletedTask;
            StartCount++;
            if (FailStart)
            {
                Publish(StudioEngineUiState.Failed, "start failed");
                throw new InvalidOperationException("start failed");
            }
            Publish(StudioEngineUiState.Running, "Em execução");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentStatus.State == StudioEngineUiState.Stopped)
                return Task.CompletedTask;
            StopCount++;
            Publish(StudioEngineUiState.Stopped, "Pronto");
            return Task.CompletedTask;
        }

        public void Publish(StudioEngineUiState state, string message)
        {
            CurrentStatus = new StudioEngineStatus(state, message);
            CurrentHealth = new StudioEngineHealth(state, message, DateTimeOffset.UtcNow);
            StatusChanged?.Invoke(this, new StudioEngineStatusChangedEventArgs(CurrentStatus));
            HealthChanged?.Invoke(this, new StudioEngineHealthChangedEventArgs(CurrentHealth));
        }
    }

    private sealed class RecordingOutputService : IStudioOutputService
    {
        private readonly TaskCompletionSource _stopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StudioOutputUiState StreamingState => StudioOutputUiState.Ready;
        public StudioOutputUiState RecordingState => StudioOutputUiState.Ready;
        public DateTimeOffset? RecordingStartedAt => null;
        public TimeSpan RecordingElapsed => TimeSpan.Zero;
        public int StopAllCount { get; private set; }
        public bool HoldStop { get; init; }
        public event EventHandler<StudioOutputStatusChangedEventArgs>? StatusChanged;
        public Task ToggleStreamingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleRecordingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAllAsync(CancellationToken cancellationToken)
        {
            StopAllCount++;
            StatusChanged?.Invoke(this, new StudioOutputStatusChangedEventArgs(StreamingState, RecordingState));
            return HoldStop ? _stopRelease.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }
        public void ReleaseStop() => _stopRelease.TrySetResult();
    }

    private sealed class RecordingSceneRuntimeService : IStudioSceneEditRuntimeService
    {
        public bool IsEngineBacked => true;
        public int DiscardAllCount { get; private set; }
        public ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(StudioDocument document, StudioScene scene, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new StudioSceneEditRuntimeSession("runtime", scene.Id, true));
        public ValueTask TrackLayerVisualStateAsync(StudioSceneEditRuntimeSession session, StudioLayer layer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask TrackSceneDraftAsync(StudioSceneEditRuntimeSession session, StudioDocument document, StudioScene originalScene, StudioScene draftScene, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(StudioSceneEditRuntimeSession session, StudioTransition? transition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new StudioSceneEditApplyResult(true, []));
        public ValueTask DiscardSceneDraftAsync(StudioSceneEditRuntimeSession session, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DiscardAllSceneDraftsAsync(CancellationToken cancellationToken = default)
        {
            DiscardAllCount++;
            return ValueTask.CompletedTask;
        }
    }
}
