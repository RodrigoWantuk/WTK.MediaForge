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
        Assert.True(state.RequiresRecordingPause);
        Assert.True(state.RequiresStreamingPause);
    }
}
