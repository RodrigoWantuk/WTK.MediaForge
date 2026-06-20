using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Capture.Tests;

[Collection("GpuCapture")]
public class GpuLifecycleStressTests
{
    [Fact]
    public async Task Provider_start_stop_dispose_leaves_no_retained_slots()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);

        try
        {
            await provider.StartAsync(CancellationToken.None);
        }
        catch
        {
            return;
        }

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            if (provider.TryAcquireLatestFrame(out var lease))
                lease.Dispose();

            await Task.Delay(16);
        }

        await provider.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();

        Assert.Equal(ProviderDisposeState.Disposed, provider.DisposeState);
        Assert.Equal(0, provider.ActiveSlotRetainCount);
        Assert.Equal(0, provider.RetiredResourceManager.PendingCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }
}
