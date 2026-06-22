using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class SourceFrameBufferTests
{
    [Fact]
    public void KeepLatest_releases_previous_frame_when_replaced()
    {
        using var buffer = new SourceFrameBuffer(new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.KeepLatest,
            Capacity = 1
        });
        var firstReleaseCount = 0;
        var secondReleaseCount = 0;

        buffer.Publish(CreateLease(1, () => firstReleaseCount++));
        buffer.Publish(CreateLease(2, () => secondReleaseCount++));

        Assert.Equal(1, firstReleaseCount);
        Assert.Equal(0, secondReleaseCount);

        buffer.Dispose();
        Assert.Equal(1, secondReleaseCount);
    }

    [Fact]
    public void Acquire_returns_latest_frame()
    {
        using var buffer = new SourceFrameBuffer();

        buffer.Publish(CreateLease(1));
        buffer.Publish(CreateLease(2));

        Assert.True(buffer.TryAcquireLatestFrame(out var lease));
        Assert.Equal(2, lease.Frame.FrameNumber);

        lease.Dispose();
    }

    [Fact]
    public void KeepLatest_source_reuses_last_frame_when_provider_has_no_new_frame()
    {
        using var buffer = new SourceFrameBuffer(new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.KeepLatest,
            Capacity = 1
        });
        buffer.Publish(CreateLease(9));

        Assert.True(buffer.TryAcquireForRender(TimeSpan.Zero, out var first));
        Assert.Equal(9, first.Frame.FrameNumber);
        first.Dispose();

        Assert.True(buffer.TryAcquireForRender(TimeSpan.FromMilliseconds(16), out var second));
        Assert.Equal(9, second.Frame.FrameNumber);
        Assert.Equal(1, buffer.Count);
        second.Dispose();
    }

    [Fact]
    public void Static_source_reuses_same_frame_across_render_ticks()
    {
        using var buffer = new SourceFrameBuffer(new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.Static,
            Capacity = 1
        });
        var firstReleaseCount = 0;
        var ignoredReleaseCount = 0;

        buffer.Publish(CreateLease(1, () => firstReleaseCount++));
        buffer.Publish(CreateLease(2, () => ignoredReleaseCount++));

        Assert.Equal(1, ignoredReleaseCount);
        Assert.True(buffer.TryAcquireForRender(TimeSpan.Zero, out var firstTick));
        Assert.True(buffer.TryAcquireForRender(TimeSpan.FromMilliseconds(16), out var secondTick));
        Assert.Equal(1, firstTick.Frame.FrameNumber);
        Assert.Equal(1, secondTick.Frame.FrameNumber);

        firstTick.Dispose();
        secondTick.Dispose();
        Assert.Equal(0, firstReleaseCount);

        buffer.Dispose();
        Assert.Equal(1, firstReleaseCount);
    }

    [Fact]
    public void Queue_source_consumes_frames_in_order()
    {
        using var buffer = new SourceFrameBuffer(new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.Queue,
            Capacity = 2
        });

        buffer.Publish(CreateLease(1));
        buffer.Publish(CreateLease(2));

        Assert.True(buffer.TryAcquireForRender(TimeSpan.Zero, out var first));
        Assert.Equal(1, first.Frame.FrameNumber);
        first.Dispose();

        Assert.True(buffer.TryAcquireForRender(TimeSpan.FromMilliseconds(16), out var second));
        Assert.Equal(2, second.Frame.FrameNumber);
        second.Dispose();

        Assert.False(buffer.TryAcquireForRender(TimeSpan.FromMilliseconds(32), out _));
    }

    [Fact]
    public void Source_buffer_dispose_releases_current_frame()
    {
        var releaseCount = 0;
        var buffer = new SourceFrameBuffer();
        buffer.Publish(CreateLease(1, () => releaseCount++));

        buffer.Dispose();

        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void Same_source_can_be_used_by_two_layers_without_double_dispose()
    {
        var releaseCount = 0;
        var buffer = new SourceFrameBuffer();
        buffer.Publish(CreateLease(1, () => releaseCount++));

        Assert.True(buffer.TryAcquireLatestFrame(out var firstLayerLease));
        Assert.True(buffer.TryAcquireLatestFrame(out var secondLayerLease));

        buffer.Dispose();
        Assert.Equal(0, releaseCount);

        firstLayerLease.Dispose();
        Assert.Equal(0, releaseCount);

        secondLayerLease.Dispose();
        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void Take_transfers_latest_frame_without_retaining_buffer_owner()
    {
        var releaseCount = 0;
        using var buffer = new SourceFrameBuffer();
        buffer.Publish(CreateLease(12, () => releaseCount++));

        Assert.True(buffer.TryTakeLatestFrame(out var lease));
        Assert.Equal(12, lease.Frame.FrameNumber);
        Assert.Equal(0, buffer.Count);

        buffer.Dispose();
        Assert.Equal(0, releaseCount);

        lease.Dispose();
        Assert.Equal(1, releaseCount);
    }

    private static GpuFrameLease CreateLease(long frameNumber, Action? onRelease = null)
    {
        var frameSize = new FrameSize(640, 480);
        return GpuFrameLease.Create(
            new GpuFrameReference
            {
                SourceId = SourceId.New(),
                Backend = GpuFrameBackend.CpuBitmap,
                TextureSize = frameSize,
                LogicalSize = frameSize,
                FrameNumber = frameNumber,
                Timestamp = MediaTime.Zero
            },
            onRelease);
    }
}
