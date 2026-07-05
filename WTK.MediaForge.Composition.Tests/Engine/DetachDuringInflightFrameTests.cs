using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Engine;

public class DetachDuringInflightFrameTests
{
    [Fact]
    public async Task DetachSink_stops_delivery_and_disposes_sink()
    {
        var sink = new RecordingPublicRenderOutputSink();
        await using var engine = EngineLifecycleTestSupport.CreateEngine();
        var project = EngineLifecycleTestSupport.CreateOffscreenProject();
        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();

        await engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id);

        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
    }

    [Fact]
    public async Task DetachSink_during_inflight_render_waits_for_submit()
    {
        var backendFactory = new BlockingSubmitRenderBackendFactory();
        var sink = new FrameNotificationSink();
        await using var engine = EngineLifecycleTestSupport.CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 30;
        var project = EngineLifecycleTestSupport.CreateOffscreenProject();
        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();

        Assert.True(backendFactory.Backend!.WaitForSubmitEntered(TimeSpan.FromSeconds(5)));

        var detachTask = engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id);
        await Task.Delay(50);
        Assert.False(detachTask.IsCompleted);

        backendFactory.Backend.ReleaseSubmit();
        Assert.True(backendFactory.Backend.WaitForSubmitExited(TimeSpan.FromSeconds(5)));
        await detachTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MediaForgeEngineState.Running, engine.State);
        await engine.StopAsync();
    }

    [Fact]
    public async Task DetachSink_releases_hung_sink_after_timeout_path()
    {
        var sink = new HungPublicRenderOutputSink();
        var engine = EngineLifecycleTestSupport.CreateEngine();
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        engine.RenderFramesPerSecond = 1;
        var project = EngineLifecycleTestSupport.CreateOffscreenProject();
        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id));

        sink.Release();
        await EngineLifecycleTestSupport.WaitUntilAsync(
            () => sink.DisposeCount == 1,
            TimeSpan.FromSeconds(5));
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Sink_worker_ignoring_cancellation_does_not_block_engine_dispose_forever()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = EngineLifecycleTestSupport.CreateOffscreenProject();
        var sink = new HungPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.DisposeAsync().AsTask());

        Assert.True(backendFactory.Backend!.Disposed || ex.InnerException is not null);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");

        sink.Release();
        await EngineLifecycleTestSupport.WaitUntilAsync(
            () => sink.DisposeCount == 1,
            TimeSpan.FromSeconds(5));
    }
}
