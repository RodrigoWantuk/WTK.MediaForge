using System.Diagnostics;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public sealed class RenderPumpTests
{
    [Fact]
    public async Task RenderPump_does_not_accumulate_pending_waits_when_timer_wins()
    {
        var published = 0;
        await using var pump = new MediaForgeRenderPump(
            framesPerSecond: 1000,
            canPublish: () => true,
            publish: () => Interlocked.Increment(ref published),
            diagnostics: null);

        await WaitUntilAsync(() => Volatile.Read(ref published) >= 20, TimeSpan.FromSeconds(5));

        var stopWatch = Stopwatch.StartNew();
        await pump.StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(stopWatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RenderPump_RequestFrame_is_not_lost_after_many_timer_ticks()
    {
        var published = 0;
        await using var pump = new MediaForgeRenderPump(
            framesPerSecond: 60,
            canPublish: () => true,
            publish: () => Interlocked.Increment(ref published),
            diagnostics: null);

        await WaitUntilAsync(() => Volatile.Read(ref published) >= 10, TimeSpan.FromSeconds(5));
        var beforeRequest = Volatile.Read(ref published);

        pump.RequestFrame();

        await WaitUntilAsync(() => Volatile.Read(ref published) > beforeRequest, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RenderPump_RequestFrame_publishes_promptly()
    {
        var published = 0;
        await using var pump = new MediaForgeRenderPump(
            framesPerSecond: 0.1,
            canPublish: () => true,
            publish: () => Interlocked.Increment(ref published),
            diagnostics: null);

        pump.RequestFrame();

        await WaitUntilAsync(() => Volatile.Read(ref published) == 1, TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task RenderPump_Stop_cancels_wait_without_leaking_tasks()
    {
        var published = 0;
        var pump = new MediaForgeRenderPump(
            framesPerSecond: 0.1,
            canPublish: () => true,
            publish: () => Interlocked.Increment(ref published),
            diagnostics: null);

        await pump.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(pump.IsRunning);
        Assert.Equal(0, Volatile.Read(ref published));
    }

    [Fact]
    public async Task RenderPump_rate_limits_backpressure_diagnostics()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        await using var pump = new MediaForgeRenderPump(
            framesPerSecond: 120,
            canPublish: () => false,
            publish: () => { },
            diagnostics);

        await Task.Delay(250);
        await pump.StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var backpressureDiagnostics = diagnostics.Diagnostics
            .Count(static diagnostic => diagnostic.Code is
                "engine.render_pump_frame_dropped_backpressure" or
                "engine.frame_scheduler_frame_dropped_backpressure");
        Assert.Equal(1, backpressureDiagnostics);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopWatch = Stopwatch.StartNew();
        while (stopWatch.Elapsed < timeout)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met before the timeout elapsed.");
    }
}
