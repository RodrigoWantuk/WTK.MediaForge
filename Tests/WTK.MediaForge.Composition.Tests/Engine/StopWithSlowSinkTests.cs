using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Engine;

public class StopWithSlowSinkTests
{
    [Fact]
    public async Task Engine_dispose_reports_sink_timeout_but_attempts_remaining_cleanup()
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

    [Fact]
    public async Task StopAsync_after_running_disposes_render_backend()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = EngineLifecycleTestSupport.CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(EngineLifecycleTestSupport.CreateValidProject());
        await engine.StartAsync();
        await engine.StopAsync();

        Assert.True(backendFactory.Backend!.Disposed);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
    }

    [Fact]
    public async Task StopAsync_after_failed_state_attempts_cleanup()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "timed-out"));
        var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        backendFactory.Backend!.ResetBindRelease();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1)));
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);

        backendFactory.Backend.ReleaseBind();
        await EngineLifecycleTestSupport.WaitUntilAsync(
            () => backendFactory.Backend.BindCount >= 1,
            TimeSpan.FromSeconds(5));

        await engine.StopAsync();

        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
        Assert.True(backendFactory.Backend.Disposed);
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task DetachSink_timeout_sets_failed_and_does_not_return_success()
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

        try
        {
            await engine.LoadProjectAsync(project);
            await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
            await engine.StartAsync();
            await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

            var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id));

            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
            Assert.IsType<TimeoutException>(ex.InnerException);
            Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");
        }
        finally
        {
            sink.Release();
            await EngineLifecycleTestSupport.WaitUntilAsync(
                () => sink.DisposeCount == 1,
                TimeSpan.FromSeconds(5));
            await engine.DisposeAsync();
        }
    }
}
