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
    public void Dispose_releases_all_pending_snapshots()
    {
        var tracker = new PendingRenderSubmissionTracker();
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(1)));
        tracker.Add(new ManualRenderFrameSubmission(CreateEmptySnapshot(2)));

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
                if (submission is not null)
                    submission.Dispose();
                else
                    snapshot.Dispose();
            }
        }

        Assert.Equal(0, source.RetainCount);
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
}
