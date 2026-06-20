using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class LatestSnapshotBufferTests
{
    [Fact]
    public void AcquireLatest_transfers_ownership_and_clears_buffer()
    {
        using var buffer = new LatestSnapshotBuffer();
        var snapshot = CreateSnapshotWithLease(frameNumber: 1);

        buffer.Publish(snapshot);

        var acquired = buffer.AcquireLatest();
        Assert.NotNull(acquired);
        Assert.Null(buffer.AcquireLatest());

        acquired!.Dispose();
    }

    [Fact]
    public void Publish_disposes_previous_snapshot()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var buffer = new LatestSnapshotBuffer();

        var first = BuildSnapshot(runtime, source.Id, 1);
        buffer.Publish(first);
        Assert.Equal(1, source.RetainCount);

        source.PublishFrame(2, new MediaTime(32_000_000));
        var second = BuildSnapshot(runtime, source.Id, 2);
        buffer.Publish(second);
        Assert.Equal(1, source.RetainCount);
    }

    [Fact]
    public void Dispose_drains_remaining_snapshot()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var buffer = new LatestSnapshotBuffer();
        buffer.Publish(BuildSnapshot(runtime, source.Id, 1));
        Assert.Equal(1, source.RetainCount);

        buffer.Dispose();
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Publish_after_dispose_throws()
    {
        var buffer = new LatestSnapshotBuffer();
        buffer.Dispose();

        var snapshot = CreateSnapshotWithLease(1);
        Assert.Throws<ObjectDisposedException>(() => buffer.Publish(snapshot));
        snapshot.Dispose();
    }

    [Fact]
    public void Publish_after_dispose_throws_and_caller_owns_snapshot()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var buffer = new LatestSnapshotBuffer();
        buffer.Dispose();

        var snapshot = BuildSnapshot(runtime, source.Id, 1);
        Assert.Equal(1, source.RetainCount);

        Assert.Throws<ObjectDisposedException>(() => buffer.Publish(snapshot));

        snapshot.Dispose();
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Publish_after_dispose_does_not_keep_snapshot_in_buffer()
    {
        var disposedBuffer = new LatestSnapshotBuffer();
        disposedBuffer.Dispose();

        var snapshot = CreateSnapshotWithLease(2);
        Assert.Throws<ObjectDisposedException>(() => disposedBuffer.Publish(snapshot));
        snapshot.Dispose();
    }

    [Fact]
    public void Concurrent_publish_and_dispose_does_not_orphan_leases()
    {
        var source = CreateRunningSource();
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var buffer = new LatestSnapshotBuffer();
        var publishErrors = 0;
        var publishIterations = 200;

        var publisher = new Thread(() =>
        {
            for (var i = 0; i < publishIterations; i++)
            {
                RenderFrameSnapshot? snapshot = null;

                try
                {
                    snapshot = BuildSnapshot(runtime, source.Id, i + 1);
                    buffer.Publish(snapshot);
                    snapshot = null;
                }
                catch (ObjectDisposedException)
                {
                    Interlocked.Increment(ref publishErrors);
                    snapshot?.Dispose();
                }
            }
        });

        var disposer = new Thread(() =>
        {
            Thread.Sleep(5);
            buffer.Dispose();
        });

        publisher.Start();
        disposer.Start();
        publisher.Join(TimeSpan.FromSeconds(10));
        disposer.Join(TimeSpan.FromSeconds(10));

        Assert.Equal(0, source.RetainCount);
    }

    private static FakeVideoFrameSource CreateRunningSource()
    {
        var sourceId = SourceId.New();
        var source = new FakeVideoFrameSource(sourceId, "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return source;
    }

    private static RenderFrameSnapshot BuildSnapshot(
        CompositionRuntime runtime,
        SourceId sourceId,
        long frameNumber)
    {
        var source = runtime.TryGetFrameProvider(sourceId, out var provider)
            ? (FakeVideoFrameSource)provider
            : throw new InvalidOperationException("Source not registered.");

        source.PublishFrame(frameNumber, new MediaTime(frameNumber * 16_000_000));

        var projectState = new ProjectStateSnapshot
        {
            Version = frameNumber,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        return result.TakeSnapshot()!;
    }

    private static RenderFrameSnapshot CreateSnapshotWithLease(long frameNumber)
    {
        var source = CreateRunningSource();
        source.PublishFrame(frameNumber, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        return BuildSnapshot(runtime, source.Id, frameNumber);
    }
}
