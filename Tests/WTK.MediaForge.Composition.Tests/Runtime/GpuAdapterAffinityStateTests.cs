using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Capture;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Infrastructure;

public sealed class GpuAdapterAffinityStateTests
{
    [Fact]
    public void Publish_and_invalidate_advance_device_generation()
    {
        var state = new GpuAdapterAffinityState();
        var generations = new List<long>();
        state.GenerationChanged += generations.Add;

        state.Publish(new GpuAdapterLuid { LowPart = 42, HighPart = 7 }, "Renderer GPU");
        var published = state.Snapshot;
        state.Invalidate();
        var invalidated = state.Snapshot;

        Assert.True(published.IsAvailable);
        Assert.Equal("Renderer GPU", published.DeviceName);
        Assert.False(invalidated.IsAvailable);
        Assert.True(invalidated.DeviceGeneration > published.DeviceGeneration);
        Assert.Equal([published.DeviceGeneration, invalidated.DeviceGeneration], generations);
    }

    [Fact]
    public void Empty_luid_cannot_be_published()
    {
        var state = new GpuAdapterAffinityState();

        Assert.Throws<ArgumentException>(() => state.Publish(GpuAdapterLuid.Empty, "Unknown"));
    }
}
