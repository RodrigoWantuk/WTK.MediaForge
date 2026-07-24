using WTK.MediaForge.Composition.Engine;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Engine;

public sealed class EngineCoordinatorLifetimeTests
{
    [Fact]
    public void Lifecycle_coordinator_exposes_single_dispose_transition()
    {
        using var coordinator = new EngineLifecycleCoordinator();
        coordinator.SetState(MediaForgeEngineState.Running);

        Assert.Equal(MediaForgeEngineState.Running, coordinator.State);
        Assert.True(coordinator.TryBeginDispose());
        Assert.False(coordinator.TryBeginDispose());
        Assert.True(coordinator.IsDisposed);
    }

    [Fact]
    public async Task Recovery_coordinator_deduplicates_and_releases_completed_key()
    {
        var coordinator = new EngineRecoveryCoordinator();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(coordinator.TryStart("source:1", () => completion.Task, _ => callback.SetResult()));
        Assert.False(coordinator.TryStart("source:1", () => Task.CompletedTask, _ => { }));
        completion.SetResult();
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(coordinator.TryStart("source:1", () => Task.CompletedTask, _ => { }));
    }
}
