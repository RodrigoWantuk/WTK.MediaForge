using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderThreadTests
{
    [Fact]
    public void NullRenderBackend_rejects_calls_off_render_thread()
    {
        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);

        Assert.Throws<InvalidOperationException>(() =>
            backend.Render(CreateEmptySnapshot(version: 1)));
    }

    [Fact]
    public void Render_thread_processes_bind_and_render_commands()
    {
        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard);

        var outputId = RenderOutputId.New();
        renderThread.EnqueueCommand(new BindOutputCommand
        {
            Binding = new RenderOutputBindingSnapshot
            {
                OutputId = outputId,
                TargetKind = RenderTargetKind.Win32Hwnd,
                NativeHandle = new IntPtr(123),
                SurfaceSize = new FrameSize(1280, 720),
                BindingVersion = 1
            }
        });

        renderThread.PublishFrame(CreateEmptySnapshot(version: 7));

        WaitUntil(() => backend.RenderCount >= 1, TimeSpan.FromSeconds(5));

        Assert.Equal(7, backend.LastProjectStateVersion);
        Assert.True(backend.Bindings.ContainsKey(outputId));
    }

    [Fact]
    public void Render_thread_disposes_snapshot_and_releases_leases()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard);

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.RenderCount >= 1, TimeSpan.FromSeconds(5));
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));

        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void RenderThread_disposes_snapshot_even_when_backend_does_not()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new NonDisposingNullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard);

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.RenderCount >= 1, TimeSpan.FromSeconds(5));
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));

        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void SlowNullRenderBackend_does_not_leak_leases_under_rapid_publish()
    {
        var source = CreateRunningSource();
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new SlowNullRenderBackend(guard, TimeSpan.FromMilliseconds(30));
        using var renderThread = StartRenderThread(backend, guard);

        for (var frame = 1; frame <= 20; frame++)
        {
            source.PublishFrame(frame, new MediaTime(frame * 16_000_000));
            renderThread.PublishFrame(BuildSnapshot(runtime, source, frame));
        }

        WaitUntil(() => backend.RenderCount >= 1, TimeSpan.FromSeconds(5));
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));

        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void PublishFrame_disposes_snapshot_when_render_thread_disposed()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);
        var renderThread = new MediaForgeRenderThread(backend, guard);
        renderThread.Start();
        renderThread.Dispose();

        var snapshot = BuildSnapshot(runtime, source, frameNumber: 1);
        Assert.Equal(1, source.RetainCount);

        Assert.Throws<ObjectDisposedException>(() => renderThread.PublishFrame(snapshot));
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Dispose_stops_thread_and_drains_pending_snapshots()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);
        var renderThread = StartRenderThread(backend, guard);
        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        renderThread.Dispose();

        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));
        Assert.False(renderThread.IsRunning);
    }

    private static MediaForgeRenderThread StartRenderThread(IRenderBackend backend, RenderThreadGuard guard)
    {
        var renderThread = new MediaForgeRenderThread(backend, guard);
        renderThread.Start();
        return renderThread;
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private static FakeVideoFrameSource CreateRunningSource()
    {
        var source = new FakeVideoFrameSource(SourceId.New(), "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return source;
    }

    private static RenderFrameSnapshot BuildSnapshot(
        CompositionRuntime runtime,
        FakeVideoFrameSource source,
        long frameNumber)
    {
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
                            SourceId = source.Id,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        return result.TakeSnapshot()!;
    }

    private static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = ImmutableArray<RenderCanvasSnapshot>.Empty,
            Outputs = ImmutableArray<RenderOutputStateSnapshot>.Empty,
            FrameLeases = ImmutableArray<GpuFrameLease>.Empty
        };
}

public sealed class NonDisposingNullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;

    public NonDisposingNullRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int RenderCount => Volatile.Read(ref _renderCount);

    private int _renderCount;

    public void BindOutput(RenderOutputBindingSnapshot binding) { }

    public void UnbindOutput(RenderOutputId outputId) { }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

    public void Render(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        Interlocked.Increment(ref _renderCount);
    }
}
