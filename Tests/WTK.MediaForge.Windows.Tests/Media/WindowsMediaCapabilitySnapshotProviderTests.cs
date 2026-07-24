using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsMediaCapabilitySnapshotProviderTests
{
    [Fact]
    public async Task Concurrent_callers_share_one_capability_probe_per_generation()
    {
        var calls = 0;
        var provider = new WindowsMediaCapabilitySnapshotProvider(async cancellationToken =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(30, cancellationToken);
            return CreateReport("adapter-a");
        });

        var snapshots = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => provider.GetAsync(CancellationToken.None).AsTask()));

        Assert.Equal(1, calls);
        Assert.All(snapshots, snapshot => Assert.Same(snapshots[0], snapshot));
        Assert.Equal("adapter-a", snapshots[0].Adapter.AdapterId);
    }

    [Fact]
    public async Task Invalidate_creates_a_new_snapshot_generation()
    {
        var calls = 0;
        var provider = new WindowsMediaCapabilitySnapshotProvider(cancellationToken =>
        {
            var call = Interlocked.Increment(ref calls);
            return ValueTask.FromResult(CreateReport($"adapter-{call}"));
        });

        var first = await provider.GetAsync();
        provider.Invalidate();
        var second = await provider.GetAsync();

        Assert.Equal(2, calls);
        Assert.NotSame(first, second);
        Assert.True(second.Generation > first.Generation);
        Assert.Equal("adapter-2", second.Adapter.AdapterId);
    }

    [Fact]
    public async Task Failed_probe_is_not_cached()
    {
        var calls = 0;
        var provider = new WindowsMediaCapabilitySnapshotProvider(cancellationToken =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return ValueTask.FromException<MediaForgeCapabilityReport>(new InvalidOperationException("Probe failed."));

            return ValueTask.FromResult(CreateReport("adapter-recovered"));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetAsync());
        var recovered = await provider.GetAsync();

        Assert.Equal(2, calls);
        Assert.Equal("adapter-recovered", recovered.Adapter.AdapterId);
    }

    private static MediaForgeCapabilityReport CreateReport(string adapterId) =>
        new()
        {
            Hardware = new HardwareMediaCapabilityReport
            {
                Platform = "Windows",
                AdapterId = adapterId,
                DeviceName = "Test GPU",
                GpuVendor = "Test Vendor"
            },
            Entries = []
        };
}
