using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Sources;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Engine;

public class RollbackFailureTests
{
    [Fact]
    public async Task StartAsync_prebound_output_backend_bind_failure_rolls_back_start()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory(throwOnBind: true);
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "prebound");
        sinkFactory.Enqueue(sink);
        await using var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(outputId, EngineLifecycleTestSupport.CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());

        Assert.False(engine.IsRunning);
        Assert.Null(engine.RenderThreadForTests);
        Assert.True(backendFactory.Backend!.Disposed);
        Assert.Same(sink, engine.GetOutputSinkForTests(outputId));
    }

    [Fact]
    public async Task StartAsync_provider_factory_failure_rolls_back_started_sources()
    {
        var providerFactory = new FakeMediaSourceProviderFactory { FailAfterCount = 1 };
        var project = EngineLifecycleTestSupport.CreateValidProject();
        project.SourceDefinitions.Add(new MediaForgeSourceDefinition
        {
            Name = "Second",
            TypeId = MediaSourceTypes.Desktop,
            Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings())
        });

        await using var engine = EngineLifecycleTestSupport.CreateEngine(providerFactory: providerFactory);
        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());
        Assert.False(engine.IsRunning);
        Assert.All(providerFactory.Sources.Values, s =>
            Assert.Equal(MediaSourceState.Stopped, s.State));
    }

    [Fact]
    public async Task StartAsync_provider_hang_times_out_and_disposes_backend()
    {
        var providerFactory = new HangingStartMediaSourceProviderFactory();
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = EngineLifecycleTestSupport.CreateEngine(providerFactory, backendFactory);
        engine.StartTimeout = TimeSpan.FromMilliseconds(50);
        engine.StopTimeout = TimeSpan.FromSeconds(1);
        await engine.LoadProjectAsync(EngineLifecycleTestSupport.CreateValidProject());

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
        Assert.Null(engine.RenderThreadForTests);
        Assert.True(backendFactory.Backend!.Disposed);
        Assert.True(providerFactory.Provider!.StopCalled);
    }

    [Fact]
    public async Task StartAsync_render_backend_factory_failure_leaves_engine_loaded()
    {
        var backendFactory = new RecordingRenderBackendFactory { ShouldFail = true };
        await using var engine = EngineLifecycleTestSupport.CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(EngineLifecycleTestSupport.CreateValidProject());

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.False(engine.IsRunning);
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
        Assert.Null(engine.RenderThreadForTests);
    }

    [Fact]
    public async Task DisposeAsync_after_start_rollback_cleans_render_thread()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory(throwOnBind: true);
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(
            EngineLifecycleTestSupport.CreatePreviewTarget(1),
            "prebound"));
        var engine = EngineLifecycleTestSupport.CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = EngineLifecycleTestSupport.CreateValidProject();
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(project.Outputs[0].Id, EngineLifecycleTestSupport.CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());
        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
    }
}
