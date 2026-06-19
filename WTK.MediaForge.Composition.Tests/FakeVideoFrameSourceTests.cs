using System.Diagnostics;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class FakeVideoFrameSourceTests
{
    [Fact]
    public async Task TryAcquireLatestFrame_returns_false_when_no_frame_ready()
    {
        var source = CreateRunningSource();

        Assert.False(source.TryAcquireLatestFrame(out _));
        await source.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TryAcquireLatestFrame_returns_latest_published_frame()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, new MediaTime(16_000_000));

        Assert.True(source.TryAcquireLatestFrame(out var lease));
        Assert.Equal(1, lease.Frame.FrameNumber);

        lease.Dispose();
        await source.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Multiple_acquires_share_same_frame_until_new_publish()
    {
        var source = CreateRunningSource();
        source.PublishFrame(7, new MediaTime(32_000_000));

        Assert.True(source.TryAcquireLatestFrame(out var first));
        Assert.True(source.TryAcquireLatestFrame(out var second));
        Assert.Equal(7, first.Frame.FrameNumber);
        Assert.Equal(7, second.Frame.FrameNumber);
        Assert.Equal(2, source.RetainCount);

        first.Dispose();
        Assert.Equal(1, source.RetainCount);

        source.PublishFrame(8, new MediaTime(48_000_000));
        Assert.True(source.TryAcquireLatestFrame(out var third));
        Assert.Equal(8, third.Frame.FrameNumber);

        second.Dispose();
        third.Dispose();
        await source.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispose_releases_retain_count()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        Assert.True(source.TryAcquireLatestFrame(out var lease));
        Assert.Equal(1, source.RetainCount);

        lease.Dispose();
        Assert.Equal(0, source.RetainCount);
        await source.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TryAcquireLatestFrame_is_non_blocking()
    {
        var source = CreateRunningSource();
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 10_000; i++)
            source.TryAcquireLatestFrame(out _);

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"Acquire took {stopwatch.ElapsedMilliseconds} ms.");

        await source.StopAsync(CancellationToken.None);
    }

    private static FakeVideoFrameSource CreateRunningSource()
    {
        var source = new FakeVideoFrameSource(SourceId.New(), "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return source;
    }
}
