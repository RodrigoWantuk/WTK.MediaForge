using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

internal static class TestGpuCaptureSupport
{
    private static readonly object ProbeGate = new();
    private static bool? s_primaryCaptureCanProduceFrames;

    public static bool TryGetPrimaryCaptureSource(out CaptureSourceInfo source)
    {
        source = null!;

        try
        {
            var monitors = DesktopMonitorEnumerator.Enumerate();
            if (monitors.Count == 0)
                return false;

            var candidate = monitors[0];
            if (!CanProduceFrames(candidate))
                return false;

            source = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanProduceFrames(CaptureSourceInfo source)
    {
        lock (ProbeGate)
        {
            if (s_primaryCaptureCanProduceFrames is { } cached)
                return cached;

            s_primaryCaptureCanProduceFrames = ProbeCanProduceFrames(source);
            return s_primaryCaptureCanProduceFrames.Value;
        }
    }

    private static bool ProbeCanProduceFrames(CaptureSourceInfo source)
    {
        using var provider = new DesktopDuplicationFrameProvider(SourceId.New(), source);

        try
        {
            provider.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3))
                .GetAwaiter()
                .GetResult();

            var deadline = Environment.TickCount64 + (long)TimeSpan.FromSeconds(5).TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (provider.TryAcquireLatestFrame(out var lease))
                {
                    lease.Dispose();
                    return true;
                }

                Thread.Sleep(20);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                provider.StopAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Test capability probing must never fail a test during cleanup.
            }
        }
    }
}
