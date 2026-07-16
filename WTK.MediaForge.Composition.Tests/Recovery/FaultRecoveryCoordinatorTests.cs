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
        Assert.False(coordinator.States.ContainsKey(FaultRecoveryScenario.VulkanDeviceLost));
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
        Assert.Contains(FaultRecoveryScenario.RenderExportFailed, coordinator.States.Keys);
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
}
