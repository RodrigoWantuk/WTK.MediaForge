using WTK.MediaForge.Composition.Runtime.Recovery;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Recovery;

public sealed class FaultRecoveryCoordinatorTests
{
    [Fact]
    public async Task Recovery_after_device_loss_restores_preview()
    {
        var coordinator = new FaultRecoveryCoordinator();
        var attempts = 0;

        var state = await coordinator.HandleFaultAsync(
            FaultRecoveryScenario.VulkanDeviceLost,
            "Simulated device loss via fault injector.",
            async _ =>
            {
                attempts++;
                await Task.Delay(10);
                return attempts >= 2;
            });

        Assert.Equal(2, attempts);
        Assert.Equal(FaultRecoveryScenario.VulkanDeviceLost, state.Scenario);
        Assert.Equal(FaultRecoveryStatus.Recovered, state.Status);
        Assert.False(coordinator.States.ContainsKey(FaultRecoveryScenario.VulkanDeviceLost.ToString()));
    }

    [Fact]
    public async Task Encoder_unavailable_pauses_recording_without_crash()
    {
        var coordinator = new FaultRecoveryCoordinator();
        coordinator.NotifyEncoderUnavailable("MF hardware encoder unavailable.");

        Assert.True(coordinator.IsRecordingPaused);
        Assert.True(coordinator.IsStreamingPaused);

        var state = await coordinator.HandleFaultAsync(
            FaultRecoveryScenario.EncoderUnavailable,
            "Encoder unavailable.",
            _ => Task.FromResult(false));

        Assert.Equal(FaultRecoveryScenario.EncoderUnavailable, state.Scenario);
        Assert.Equal(FaultRecoveryStatus.Exhausted, state.Status);
        Assert.True(state.RequiresRecordingPause);
        Assert.True(state.RequiresStreamingPause);
    }

    [Fact]
    public async Task Rtmp_disconnect_pauses_streaming_without_pausing_recording()
    {
        var coordinator = new FaultRecoveryCoordinator(policyProvider: scenario =>
            FaultRecoveryPolicy.ForScenario(scenario) with
            {
                MaxAttempts = 2,
                InitialBackoff = TimeSpan.Zero,
                MaxBackoff = TimeSpan.Zero
            });

        var state = await coordinator.HandleFaultAsync(
            FaultRecoveryScenario.RtmpDisconnected,
            "Socket disconnected.",
            _ => Task.FromResult(false));

        Assert.Equal(FaultRecoveryStatus.Exhausted, state.Status);
        Assert.False(state.RequiresRecordingPause);
        Assert.True(state.RequiresStreamingPause);
        Assert.False(coordinator.IsRecordingPaused);
        Assert.True(coordinator.IsStreamingPaused);
    }

    [Fact]
    public void Render_export_failure_pauses_encoded_outputs()
    {
        var coordinator = new FaultRecoveryCoordinator();

        coordinator.NotifyRenderExportFailed("Vulkan export failed.");

        Assert.True(coordinator.IsRecordingPaused);
        Assert.True(coordinator.IsStreamingPaused);
        Assert.Contains("render-export", coordinator.States.Keys);
    }

    [Fact]
    public void Source_provider_failure_isolates_source_without_pausing_outputs()
    {
        var coordinator = new FaultRecoveryCoordinator();

        coordinator.NotifySourceProviderFailed("Webcam unplugged.");

        Assert.True(coordinator.HasIsolatedSources);
        Assert.False(coordinator.IsRecordingPaused);
        Assert.False(coordinator.IsStreamingPaused);
    }

    [Fact]
    public async Task Concurrent_recovery_for_same_resource_is_serialized()
    {
        var coordinator = new FaultRecoveryCoordinator(policyProvider: scenario =>
            FaultRecoveryPolicy.ForScenario(scenario) with
            {
                MaxAttempts = 1,
                InitialBackoff = TimeSpan.Zero,
                MaxBackoff = TimeSpan.Zero
            });
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentActions = 0;
        var maximumConcurrentActions = 0;

        async Task<bool> RecoverAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref concurrentActions);
            UpdateMaximum(ref maximumConcurrentActions, current);
            firstEntered.TrySetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrentActions);
            return true;
        }

        var first = coordinator.HandleFaultAsync(
            FaultRecoveryScenario.SourceProviderFailed,
            "source:webcam-1",
            "Capture failed.",
            RecoverAsync);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = coordinator.HandleFaultAsync(
            FaultRecoveryScenario.SourceProviderFailed,
            "source:webcam-1",
            "Capture failed again.",
            RecoverAsync);

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, maximumConcurrentActions);
    }

    [Fact]
    public async Task Concurrent_faults_for_different_resources_keep_independent_state()
    {
        var coordinator = new FaultRecoveryCoordinator();

        coordinator.NotifySourceProviderFailed("Webcam one failed.");
        await coordinator.HandleFaultAsync(
            FaultRecoveryScenario.SourceProviderFailed,
            "source:webcam-2",
            "Webcam two failed.",
            _ => Task.FromResult(false));

        Assert.Contains("source", coordinator.States.Keys);
        Assert.Contains("source:webcam-2", coordinator.States.Keys);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (candidate <= observed || Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
                return;
        }
    }
}
