using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class FrameSchedulerTests
{
    [Fact]
    public async Task Frame_scheduler_publishes_execution_context_with_target_outputs()
    {
        var outputA = RenderOutputId.New();
        var outputB = RenderOutputId.New();
        FrameExecutionContext? published = null;

        await using var scheduler = new FrameScheduler(
            framesPerSecond: 30,
            canPublish: static () => true,
            publish: context => published = context,
            targetOutputs: () => [outputA, outputB],
            diagnostics: null);

        scheduler.RequestFrame();

        await WaitForConditionAsync(
            () => published is not null,
            TimeSpan.FromSeconds(2));

        Assert.NotNull(published);
        Assert.Equal(2, published!.TargetOutputs.Count);
        Assert.Contains(outputA, published.TargetOutputs);
        Assert.Contains(outputB, published.TargetOutputs);
        Assert.True(published.FrameId >= 1);
        Assert.True(published.FrameBudget > TimeSpan.Zero);
        Assert.True(published.PresentationTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Frame_scheduler_presentation_time_increments_at_target_frame_rate()
    {
        var published = new List<FrameExecutionContext>();

        await using var scheduler = new FrameScheduler(
            framesPerSecond: 30,
            canPublish: static () => true,
            publish: context => published.Add(context),
            targetOutputs: static () => Array.Empty<RenderOutputId>(),
            diagnostics: null);

        scheduler.RequestFrame();

        await WaitForConditionAsync(
            () => published.Count >= 2,
            TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(1d / 30d), published[0].PresentationTime);
        Assert.Equal(published[0].PresentationTime + scheduler.FrameBudget, published[1].PresentationTime);
    }

    [Fact]
    public async Task Frame_scheduler_drops_frames_when_backpressured()
    {
        var publishCount = 0;

        await using var scheduler = new FrameScheduler(
            framesPerSecond: 1000,
            canPublish: static () => false,
            publish: _ => Interlocked.Increment(ref publishCount),
            targetOutputs: static () => Array.Empty<RenderOutputId>(),
            diagnostics: null);

        scheduler.RequestFrame();
        scheduler.RequestFrame();

        await Task.Delay(100);

        Assert.Equal(0, publishCount);
    }

    [Fact]
    public async Task Stop_timeout_retains_scheduler_ownership_until_retry_completes()
    {
        using var publishEntered = new ManualResetEventSlim();
        using var releasePublish = new ManualResetEventSlim();
        await using var scheduler = new FrameScheduler(
            framesPerSecond: 60,
            canPublish: static () => true,
            publish: _ =>
            {
                publishEntered.Set();
                releasePublish.Wait();
            },
            targetOutputs: static () => Array.Empty<RenderOutputId>(),
            diagnostics: null);

        scheduler.RequestFrame();
        Assert.True(publishEntered.Wait(TimeSpan.FromSeconds(2)));

        var timeout = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await scheduler.StopAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
        Assert.Contains("ownership is retained", timeout.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(scheduler.IsRunning);

        releasePublish.Set();
        await scheduler.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.False(scheduler.IsRunning);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        if (condition())
            return;

        throw new TimeoutException("Condition was not met before timeout.");
    }
}
