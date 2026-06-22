using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

[Collection("RenderThread")]
public class GpuFrameSlotRingIntegrationTests
{
    [Fact]
    public async Task Lease_keeps_slot_retained_until_submission_completes()
    {
        var sourceId = SourceId.New();
        var source = new FakeGpuFrameSlotRingVideoFrameSource(sourceId, "Ring", new FrameSize(640, 480));
        await source.StartAsync(CancellationToken.None);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        using var renderThread = new MediaForgeRenderThread(backend, guard, maxFramesInFlight: 4);
        renderThread.Start();

        Assert.True(source.TryCaptureFrame(frameNumber: 1, MediaTime.Zero));
        renderThread.PublishFrame(BuildSnapshot(runtime, sourceId, frameNumber: 1));

        WaitUntil(() => backend.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.ActiveSlotRetainCount);

        backend.CompleteAllPending();
        WaitUntil(() => source.ActiveSlotRetainCount == 1, TimeSpan.FromSeconds(5));
        renderThread.Dispose();
        runtime.Dispose();
        Assert.Equal(0, source.ActiveSlotRetainCount);
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

    private static RenderFrameSnapshot BuildSnapshot(
        CompositionRuntime runtime,
        SourceId sourceId,
        long frameNumber)
    {
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
}
