using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class PendingRenderSubmissionTrackerTests
{
    [Fact]
    public void Add_throws_when_tracker_disposed()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var submission = new ImmediateRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.Add(submission));
        submission.Dispose();
    }

    [Fact]
    public void PollCompleted_disposes_completed_submissions()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var manual = new ManualRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(manual);

        tracker.PollCompleted();
        Assert.Equal(1, tracker.PendingCount);

        manual.Complete();
        tracker.PollCompleted();
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void DisposeCompleted_throws_when_submission_not_completed()
    {
        var submission = new ManualRenderFrameSubmission(CreateEmptySnapshot(1));

        Assert.Throws<InvalidOperationException>(() => submission.DisposeCompleted());
    }

    [Fact]
    public void DisposeCompleted_is_idempotent_when_submission_completed()
    {
        var submission = new ImmediateRenderFrameSubmission(CreateEmptySnapshot(1));

        submission.DisposeCompleted();
        submission.DisposeCompleted();
    }

    [Fact]
    public async Task ShutdownAsync_waits_for_incomplete_submissions()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var manual = new ManualRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(manual);

        var shutdownTask = tracker.ShutdownAsync(
            new ImmediateIdleRenderBackend(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await Task.Delay(50);
        manual.Complete();

        await shutdownTask;
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void Dispose_releases_all_pending_snapshots()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new ImmediateRenderFrameSubmission(CreateEmptySnapshot(1)));
        tracker.Add(new ImmediateRenderFrameSubmission(CreateEmptySnapshot(2)));

        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.Add(
            new ImmediateRenderFrameSubmission(CreateEmptySnapshot(3))));
    }

    [Fact]
    public void CanAcceptFrame_respects_max_frames_in_flight()
    {
        var tracker = new PendingRenderSubmissionTracker(maxFramesInFlight: 2);
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(1)));

        Assert.True(tracker.CanAcceptFrame);
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(2)));
        Assert.False(tracker.CanAcceptFrame);
    }

    [Fact]
    public void Submit_returns_submission_but_tracker_add_fails_disposes_submission()
    {
        var source = new FakeVideoFrameSource(SourceId.New(), "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var buildResult = RenderFrameSnapshotFactory.Build(
            new ProjectStateSnapshot
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
                                SourceId = source.Id,
                                Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                            }
                        ]
                    }
                ]
            },
            runtime);

        var snapshot = buildResult.TakeSnapshot()!;
        Assert.Equal(1, source.RetainCount);

        IRenderFrameSubmission? submission = null;
        var ownershipTransferred = false;
        var tracker = new ThrowOnAddPendingRenderSubmissionTracker();

        try
        {
            submission = new ImmediateRenderFrameSubmission(snapshot);
            tracker.Add(submission);
            ownershipTransferred = true;
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (submission is ImmediateRenderFrameSubmission immediate)
                    immediate.Dispose();
                else
                    snapshot.Dispose();
            }
        }

        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void PollCompleted_does_not_remove_submission_when_DisposeCompleted_fails()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var submission = new FailingRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(submission);

        tracker.PollCompleted();

        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public void Failed_dispose_can_be_retried_on_next_poll()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var submission = new FailOnceRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(submission);

        tracker.PollCompleted();
        Assert.Equal(1, tracker.PendingCount);

        tracker.PollCompleted();
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void PollCompleted_reports_dispose_failure_once_while_keeping_submission_pending()
    {
        var sink = new InMemoryDiagnosticsSink();
        var tracker = new PendingRenderSubmissionTracker(diagnostics: sink);
        var submission = new FailingRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(submission);

        tracker.PollCompleted();
        tracker.PollCompleted();
        tracker.PollCompleted();

        Assert.Equal(1, tracker.PendingCount);
        Assert.Single(sink.Diagnostics);
        Assert.Equal("render.submission_dispose_failed", sink.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ShutdownAsync_propagates_DisposeCompleted_failure()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new FailingRenderFrameSubmission(CreateEmptySnapshot(1)));

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            tracker.ShutdownAsync(
                new ImmediateIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.NotEmpty(ex.InnerExceptions);
    }

    [Fact]
    public async Task ShutdownAsync_attempts_all_submissions_even_when_one_dispose_fails()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var failing = new FailingRenderFrameSubmission(CreateEmptySnapshot(1));
        var succeeding = new ImmediateRenderFrameSubmission(CreateEmptySnapshot(2));
        tracker.Add(failing);
        tracker.Add(succeeding);

        await Assert.ThrowsAsync<AggregateException>(() =>
            tracker.ShutdownAsync(
                new ImmediateIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public async Task Add_throws_when_shutdown_in_progress()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new FailingRenderFrameSubmission(CreateEmptySnapshot(1)));

        await Assert.ThrowsAsync<AggregateException>(() =>
            tracker.ShutdownAsync(
                new ImmediateIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.Throws<InvalidOperationException>(() =>
            tracker.Add(new ImmediateRenderFrameSubmission(CreateEmptySnapshot(2))));
    }

    [Fact]
    public async Task ShutdownAsync_failure_keeps_tracker_in_shutdown_state()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new FailingRenderFrameSubmission(CreateEmptySnapshot(1)));

        await Assert.ThrowsAsync<AggregateException>(() =>
            tracker.ShutdownAsync(
                new ImmediateIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.False(tracker.CanAcceptFrame);
        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public async Task ShutdownAsync_retry_can_cleanup_remaining_pending()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var submission = new RecoverableFailingRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(submission);

        await Assert.ThrowsAsync<AggregateException>(() =>
            tracker.ShutdownAsync(
                new ImmediateIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.Equal(1, tracker.PendingCount);

        submission.AllowDispose();

        await tracker.ShutdownAsync(
            new ImmediateIdleRenderBackend(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public async Task Add_throws_immediately_after_ShutdownAsync_starts()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var manual = new ManualRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(manual);

        var backend = new DelayedWaitIdleRenderBackend();
        var shutdownTask = tracker.ShutdownAsync(
            backend,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await backend.WaitUntilWaitIdleEnteredAsync(TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() =>
            tracker.Add(new ImmediateRenderFrameSubmission(CreateEmptySnapshot(2))));

        backend.ReleaseWaitIdle();
        manual.Complete();
        await shutdownTask;
    }

    [Fact]
    public async Task ShutdownAsync_waitIdle_failure_keeps_tracker_in_shutdown_state()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracker.ShutdownAsync(
                new FailingWaitIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.False(tracker.CanAcceptFrame);
        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public async Task Add_throws_after_waitIdle_failure_during_shutdown()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracker.ShutdownAsync(
                new FailingWaitIdleRenderBackend(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        Assert.Throws<InvalidOperationException>(() =>
            tracker.Add(new ImmediateRenderFrameSubmission(CreateEmptySnapshot(2))));
    }

    [Fact]
    public async Task ShutdownAsync_waitIdle_failure_can_retry_after_backend_recovers()
    {
        var tracker = new PendingRenderSubmissionTracker();
        var manual = new ManualRenderFrameSubmission(CreateEmptySnapshot(1));
        tracker.Add(manual);

        var backend = new FailOnceWaitIdleRenderBackend();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracker.ShutdownAsync(
                backend,
                TimeSpan.FromSeconds(5),
                CancellationToken.None).AsTask());

        manual.Complete();

        await tracker.ShutdownAsync(
            backend,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(0, tracker.PendingCount);
    }

    private static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = ImmutableArray<RenderCanvasSnapshot>.Empty,
            Outputs = ImmutableArray<RenderOutputStateSnapshot>.Empty,
            FrameLeases = ImmutableArray<Core.Gpu.GpuFrameLease>.Empty
        };

    private sealed class ThrowOnAddPendingRenderSubmissionTracker : PendingRenderSubmissionTracker
    {
        public override void Add(IRenderFrameSubmission submission) =>
            throw new InvalidOperationException("Simulated tracker add failure.");
    }

    private sealed class ImmediateIdleRenderBackend : IRenderBackend
    {
        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) =>
            new ImmediateRenderFrameSubmission(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class DelayedWaitIdleRenderBackend : IRenderBackend
    {
        private readonly TaskCompletionSource _waitIdleEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWaitIdle =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitUntilWaitIdleEnteredAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _waitIdleEntered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }

        public void ReleaseWaitIdle() => _releaseWaitIdle.TrySetResult();

        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) =>
            new ImmediateRenderFrameSubmission(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            new(WaitIdleAsyncCore(cancellationToken));

        private async Task WaitIdleAsyncCore(CancellationToken cancellationToken)
        {
            _waitIdleEntered.TrySetResult();
            await _releaseWaitIdle.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FailingWaitIdleRenderBackend : IRenderBackend
    {
        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) =>
            new ImmediateRenderFrameSubmission(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Simulated WaitIdle failure."));
    }

    private sealed class FailOnceWaitIdleRenderBackend : IRenderBackend
    {
        private int _waitIdleCalls;

        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) =>
            new ImmediateRenderFrameSubmission(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _waitIdleCalls) == 1)
            {
                return ValueTask.FromException(new InvalidOperationException("Simulated WaitIdle failure."));
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingRenderFrameSubmission : IRenderFrameSubmission
    {
        public FailingRenderFrameSubmission(RenderFrameSnapshot snapshot) =>
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        public RenderFrameSnapshot Snapshot { get; }

        public bool IsCompleted => true;

        public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void DisposeCompleted() =>
            throw new InvalidOperationException("Simulated dispose failure.");
    }

    private sealed class RecoverableFailingRenderFrameSubmission : IRenderFrameSubmission
    {
        private volatile bool _allowDispose;

        public RecoverableFailingRenderFrameSubmission(RenderFrameSnapshot snapshot) =>
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        public RenderFrameSnapshot Snapshot { get; }

        public bool IsCompleted => true;

        public void AllowDispose() => _allowDispose = true;

        public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void DisposeCompleted()
        {
            if (!_allowDispose)
                throw new InvalidOperationException("Simulated dispose failure.");
        }
    }

    private sealed class FailOnceRenderFrameSubmission : IRenderFrameSubmission
    {
        private int _disposeAttempts;

        public FailOnceRenderFrameSubmission(RenderFrameSnapshot snapshot) =>
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        public RenderFrameSnapshot Snapshot { get; }

        public bool IsCompleted => true;

        public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void DisposeCompleted()
        {
            if (Interlocked.Increment(ref _disposeAttempts) == 1)
                throw new InvalidOperationException("Simulated first dispose failure.");
        }
    }
}
