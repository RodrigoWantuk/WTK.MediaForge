using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Runtime;

public sealed class MediaForgeRuntimeContractsTests
{
    [Fact]
    public async Task Unavailable_runtime_keeps_capability_truth_and_does_not_create_an_engine()
    {
        var probe = new NullHardwareMediaCapabilityProbe();
        var expected = new MediaForgeCapabilitySnapshot
        {
            Generation = 7,
            CapturedAt = DateTimeOffset.UtcNow,
            Adapter = new MediaForgeHardwareAdapterInfo
            {
                Platform = "Test",
                AdapterId = "none",
                DeviceName = "No adapter",
                DeviceGeneration = 7
            },
            Report = MediaForgeCapabilityReportBuilder.Build(await probe.ProbeAsync())
        };
        var runtime = MediaForgeRuntime.Unavailable(
            "No backend.", probe, MediaForgeRuntimeAdapterCatalog.Known, _ => ValueTask.FromResult(expected));

        Assert.Equal(MediaForgeRuntimeAvailability.Unavailable, runtime.Availability);
        Assert.Null(runtime.Engine);
        Assert.Equal("No backend.", runtime.UnavailableReason);
        Assert.Same(expected, await runtime.GetCapabilitySnapshotAsync());
        await runtime.DisposeAsync();
    }
}
