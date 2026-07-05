using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Engine;

public class SinkRebindFailureTests
{
    [Fact]
    public async Task BindOutput_CreateBinding_failure_keeps_existing_sink()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "old");
        var newSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(2),
            "new") { ThrowOnCreateBinding = true };
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = EngineLifecycleTestSupport.CreateEngine(outputSinkFactory: sinkFactory);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(2)));

        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
        Assert.Equal(1, newSink.DisposeCount);
    }

    [Fact]
    public async Task BindOutput_command_timeout_sets_failed_and_disposes_new_sink()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "old"));
        var newSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(2),
            "new");
        sinkFactory.Enqueue(newSink);
        var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1));
        backendFactory.Backend!.ResetBindRelease();

        try
        {
            var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(2)));

            Assert.IsType<TimeoutException>(ex.InnerException);
            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
            Assert.Equal(1, newSink.DisposeCount);
        }
        finally
        {
            backendFactory.Backend.ReleaseBind();
            await engine.DisposeAsync();
        }

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.True(backendFactory.Backend.Disposed);
    }

    [Fact]
    public async Task BindOutput_backend_failure_keeps_existing_sink_and_throws_to_caller()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "old");
        var newSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(2),
            "new");
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory,
            diagnostics: diagnostics);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1));

        backendFactory.Backend!.ThrowOnBind = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(2)));

        Assert.Contains("Configured bind command failure", ex.Message, StringComparison.Ordinal);
        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
        Assert.Equal(1, newSink.DisposeCount);
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "render.command_failed");
    }

    [Fact]
    public async Task BindOutput_CreateSink_failure_keeps_existing_sink()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "old");
        sinkFactory.Enqueue(oldSink);
        await using var engine = EngineLifecycleTestSupport.CreateEngine(outputSinkFactory: sinkFactory);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1));

        sinkFactory.ThrowOnCreateSink = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(2)));

        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
    }
}
