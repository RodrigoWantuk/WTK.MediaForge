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
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

[Collection("RenderThread")]
public class RenderThreadTests
{
    [Fact]
    public void NullRenderBackend_rejects_calls_off_render_thread()
    {
        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);

        Assert.Throws<InvalidOperationException>(() =>
            backend.Submit(CreateEmptySnapshot(version: 1)));
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
    public void Completed_submission_disposes_snapshot_and_releases_lease()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard, maxFramesInFlight: 4);

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.RetainCount);

        backend.CompleteAllPending();
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Submit_accepted_keeps_snapshot_alive_until_submission_completed()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard, maxFramesInFlight: 4);

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.RetainCount);
        Assert.Equal(1, renderThread.PendingTracker.PendingCount);

        backend.CompleteAllPending();
    }

    [Fact]
    public void Pending_tracker_limits_frames_in_flight()
    {
        var source = CreateRunningSource();
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard, maxFramesInFlight: 1);

        for (var frame = 1; frame <= 5; frame++)
        {
            source.PublishFrame(frame, new MediaTime(frame * 16_000_000));
            renderThread.PublishFrame(BuildSnapshot(runtime, source, frame));
        }

        WaitUntil(() => backend.SubmitCount >= 1, TimeSpan.FromSeconds(5));

        Assert.True(renderThread.PendingTracker.PendingCount <= 1);

        backend.CompleteAllPending();
        WaitUntil(() => renderThread.PendingTracker.PendingCount == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Render_thread_reports_diagnostic_when_tracker_full()
    {
        var source = CreateRunningSource();
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var diagnostics = new ListDiagnosticsSink();
        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        var renderThread = StartRenderThread(backend, guard, maxFramesInFlight: 1, diagnostics: diagnostics);

        try
        {
            source.PublishFrame(1, MediaTime.Zero);
            renderThread.PublishFrame(BuildSnapshot(runtime, source, 1));

            WaitUntil(
                () => backend.SubmitCount >= 1 && renderThread.PendingTracker.PendingCount == 1,
                TimeSpan.FromSeconds(5));

            source.PublishFrame(2, new MediaTime(16_000_000));
            renderThread.PublishFrame(BuildSnapshot(runtime, source, 2));

            WaitUntil(
                () => diagnostics.Diagnostics.Any(d => d.Code == "render.frame_dropped_tracker_full"),
                TimeSpan.FromSeconds(5));

            WaitUntil(
                () =>
                {
                    backend.CompleteAllPending();
                    return renderThread.PendingTracker.PendingCount == 0;
                },
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            renderThread.Dispose();
        }
    }

    [Fact]
    public void If_submit_throws_render_thread_disposes_snapshot()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new ThrowingSubmitNullRenderBackend(guard);
        using var renderThread = StartRenderThread(backend, guard);

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.SubmitAttempts >= 1, TimeSpan.FromSeconds(5));
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Submit_returns_submission_but_tracker_add_fails_disposes_submission()
    {
        var source = CreateRunningSource();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new NullRenderBackend(guard);
        var tracker = new ThrowOnAddPendingRenderSubmissionTracker();
        using var renderThread = new MediaForgeRenderThread(backend, guard, tracker);
        renderThread.Start();

        renderThread.PublishFrame(BuildSnapshot(runtime, source, frameNumber: 1));

        WaitUntil(() => backend.RenderCount >= 1, TimeSpan.FromSeconds(5));
        WaitUntil(() => source.RetainCount == 0, TimeSpan.FromSeconds(5));
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
    public void Dispose_runs_backend_WaitIdleAsync_on_render_thread()
    {
        var guard = new RenderThreadGuard();
        var backend = new WaitIdleTrackingRenderBackend(guard);
        var renderThread = StartRenderThread(backend, guard);
        renderThread.Dispose();

        Assert.True(backend.WaitIdleCalledOnRenderThread);
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

    [Fact]
    public void Render_thread_does_not_dispose_work_signal_when_join_times_out()
    {
        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(version: 1)));

        var renderThread = new MediaForgeRenderThread(
            backend,
            guard,
            tracker,
            joinTimeout: TimeSpan.FromMilliseconds(50));

        renderThread.Start();

        Assert.Throws<TimeoutException>(() => renderThread.Dispose());
        Assert.False(renderThread.WorkSignalDisposedForTests);
    }

    private static MediaForgeRenderThread StartRenderThread(
        IRenderBackend backend,
        RenderThreadGuard guard,
        int maxFramesInFlight = 2,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        var renderThread = new MediaForgeRenderThread(
            backend,
            guard,
            maxFramesInFlight: maxFramesInFlight,
            diagnostics: diagnostics);
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

    private sealed class ThrowOnAddPendingRenderSubmissionTracker : PendingRenderSubmissionTracker
    {
        public override void Add(IRenderFrameSubmission submission) =>
            throw new InvalidOperationException("Simulated tracker add failure.");
    }

    private sealed class WaitIdleTrackingRenderBackend : IRenderBackend
    {
        private readonly RenderThreadGuard _threadGuard;

        public WaitIdleTrackingRenderBackend(RenderThreadGuard threadGuard) =>
            _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

        public bool WaitIdleCalledOnRenderThread { get; private set; }

        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) =>
            new ImmediateRenderFrameSubmission(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            _threadGuard.AssertOnRenderThread();
            WaitIdleCalledOnRenderThread = true;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class ManualNullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly List<ManualRenderFrameSubmission> _pending = [];

    public ManualNullRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int SubmitCount => Volatile.Read(ref _submitCount);

    public int PendingBackendSubmissionCount
    {
        get
        {
            lock (_pending)
                return _pending.Count;
        }
    }

    private int _submitCount;

    public void BindOutput(RenderOutputBindingSnapshot binding) { }

    public void UnbindOutput(RenderOutputId outputId) { }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _submitCount);
        var submission = new ManualRenderFrameSubmission(snapshot);

        lock (_pending)
            _pending.Add(submission);

        return submission;
    }

    public void CompleteAllPending()
    {
        ManualRenderFrameSubmission[] copy;

        lock (_pending)
        {
            copy = [.. _pending];
            _pending.Clear();
        }

        foreach (var submission in copy)
            submission.Complete();
    }

    public bool Disposed { get; private set; }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Dispose() => Disposed = true;
}

internal sealed class ThrowingSubmitNullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;

    public ThrowingSubmitNullRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int SubmitAttempts => Volatile.Read(ref _submitAttempts);

    private int _submitAttempts;

    public void BindOutput(RenderOutputBindingSnapshot binding) { }

    public void UnbindOutput(RenderOutputId outputId) { }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        Interlocked.Increment(ref _submitAttempts);
        throw new InvalidOperationException("Simulated submit failure.");
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}
