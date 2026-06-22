using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class SourceRuntimeManagerTests
{
    [Fact]
    public async Task SourceRuntimeManager_starts_all_sources()
    {
        using var manager = new SourceRuntimeManager();
        var first = new FakeVideoFrameSource(SourceId.New(), "First", new FrameSize(640, 480));
        var second = new FakeVideoFrameSource(SourceId.New(), "Second", new FrameSize(640, 480));
        manager.RegisterProvider(first);
        manager.RegisterProvider(second);

        await manager.StartAllAsync(CancellationToken.None);

        Assert.Equal(MediaSourceState.Running, first.State);
        Assert.Equal(MediaSourceState.Running, second.State);
    }

    [Fact]
    public async Task SourceRuntimeManager_rolls_back_when_source_start_fails()
    {
        using var manager = new SourceRuntimeManager();
        var started = new RecordingVideoFrameProvider("Started");
        var failing = new RecordingVideoFrameProvider("Failing") { ThrowOnStart = true };
        manager.RegisterProvider(started);
        manager.RegisterProvider(failing);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StartAllAsync(CancellationToken.None));

        Assert.Equal(1, started.StopCount);
        Assert.Equal(0, failing.StopCount);
    }

    [Fact]
    public async Task SourceRuntimeManager_stops_all_sources_even_when_one_stop_fails()
    {
        using var manager = new SourceRuntimeManager();
        var first = new RecordingVideoFrameProvider("First") { ThrowOnStop = true };
        var second = new RecordingVideoFrameProvider("Second");
        manager.RegisterProvider(first);
        manager.RegisterProvider(second);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            manager.StopAllAsync(CancellationToken.None));

        Assert.Single(ex.InnerExceptions);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
    }

    [Fact]
    public async Task SourceRuntimeManager_try_acquire_frame_returns_latest_live_frame()
    {
        using var manager = new SourceRuntimeManager();
        var source = new FakeVideoFrameSource(SourceId.New(), "Fake", new FrameSize(640, 480));
        manager.RegisterProvider(source);
        await source.StartAsync(CancellationToken.None);
        source.PublishFrame(42, MediaTime.Zero);

        var result = manager.TryAcquireFrame(source.Id, TimeSpan.Zero);

        Assert.Equal(SourceFrameAcquireStatus.Acquired, result.Status);
        Assert.Equal(42, result.Lease!.Frame.FrameNumber);
        result.Lease.Dispose();
        Assert.Equal(1, source.RetainCount);

        manager.Clear();
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Source_runtime_keep_latest_does_not_return_NoFrame_between_provider_frames()
    {
        using var manager = new SourceRuntimeManager();
        var source = new EdgeTriggeredVideoFrameProvider(SourceId.New(), "Edge");
        manager.RegisterProvider(source, MediaSourceTypeId.From("test.edge"), new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.KeepLatest,
            Capacity = 1
        });
        source.PublishFrame(7);

        var first = manager.TryAcquireFrame(source.Id, TimeSpan.Zero);
        Assert.Equal(SourceFrameAcquireStatus.Acquired, first.Status);
        Assert.Equal(7, first.Lease!.Frame.FrameNumber);
        first.Lease.Dispose();

        var second = manager.TryAcquireFrame(source.Id, TimeSpan.FromMilliseconds(16));
        Assert.Equal(SourceFrameAcquireStatus.Acquired, second.Status);
        Assert.Equal(7, second.Lease!.Frame.FrameNumber);
        second.Lease.Dispose();
    }

    [Fact]
    public void Same_source_used_by_two_layers_without_double_dispose()
    {
        using var runtime = new CompositionRuntime();
        var sourceId = SourceId.New();
        var source = new EdgeTriggeredVideoFrameProvider(sourceId, "Edge");
        runtime.RegisterFrameProvider(source);
        source.PublishFrame(11);

        using (var result = RenderFrameSnapshotFactory.Build(CreateTwoLayerProjectState(sourceId), runtime))
        {
            var snapshot = result.TakeSnapshot();
            Assert.NotNull(snapshot);
            Assert.Single(snapshot!.FrameLeases);

            var first = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot.Canvases[0].Objects[0]);
            var second = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot.Canvases[0].Objects[1]);
            Assert.Equal(11, first.BoundFrame!.Value.FrameNumber);
            Assert.Equal(11, second.BoundFrame!.Value.FrameNumber);

            snapshot.Dispose();
        }

        Assert.Equal(0, source.ReleaseCount);
        runtime.Dispose();
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public void SourceRuntimeManager_reports_failed_acquire_without_throwing()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        using var manager = new SourceRuntimeManager(diagnostics);
        var source = new ThrowingAcquireProvider(SourceId.New(), "Bad");
        manager.RegisterProvider(source);

        var result = manager.TryAcquireFrame(source.Id, TimeSpan.Zero);

        Assert.Equal(SourceFrameAcquireStatus.SourceFailed, result.Status);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "source.frame_acquire_failed");
    }

    private sealed class RecordingVideoFrameProvider(string name) : IVideoFrameProvider
    {
        public SourceId Id { get; } = SourceId.New();

        public string Name { get; } = name;

        public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

        public Exception? LastError { get; private set; }

        public bool ThrowOnStart { get; init; }

        public bool ThrowOnStop { get; init; }

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnStart)
                throw new InvalidOperationException("Configured source start failure.");

            State = MediaSourceState.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            State = MediaSourceState.Stopped;

            if (ThrowOnStop)
                throw new InvalidOperationException("Configured source stop failure.");

            return Task.CompletedTask;
        }

        public bool TryAcquireLatestFrame(out GpuFrameLease lease)
        {
            lease = null!;
            return false;
        }
    }

    private sealed class ThrowingAcquireProvider(SourceId id, string name) : IVideoFrameProvider
    {
        public SourceId Id { get; } = id;

        public string Name { get; } = name;

        public MediaSourceState State => MediaSourceState.Running;

        public Exception? LastError => null;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryAcquireLatestFrame(out GpuFrameLease lease) =>
            throw new InvalidOperationException("Configured source acquire failure.");
    }

    private sealed class EdgeTriggeredVideoFrameProvider(SourceId id, string name) : IVideoFrameProvider
    {
        private readonly object _gate = new();
        private GpuFrameReference? _nextFrame;

        public SourceId Id { get; } = id;

        public string Name { get; } = name;

        public MediaSourceState State => MediaSourceState.Running;

        public Exception? LastError => null;

        public int ReleaseCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void PublishFrame(long frameNumber)
        {
            var frameSize = new FrameSize(640, 480);
            lock (_gate)
            {
                _nextFrame = new GpuFrameReference
                {
                    SourceId = Id,
                    Backend = GpuFrameBackend.CpuBitmap,
                    TextureSize = frameSize,
                    LogicalSize = frameSize,
                    FrameNumber = frameNumber,
                    Timestamp = new MediaTime(frameNumber)
                };
            }
        }

        public bool TryAcquireLatestFrame(out GpuFrameLease lease)
        {
            lock (_gate)
            {
                if (_nextFrame is not { } frame)
                {
                    lease = null!;
                    return false;
                }

                _nextFrame = null;
                lease = GpuFrameLease.Create(frame, () => ReleaseCount++);
                return true;
            }
        }
    }

    private static ProjectStateSnapshot CreateTwoLayerProjectState(SourceId sourceId) =>
        new()
        {
            Version = 1,
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
                            Name = "Layer A",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        },
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer B",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) }
                        }
                    ]
                }
            ]
        };
}
