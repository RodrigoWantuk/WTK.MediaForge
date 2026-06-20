using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Tests.Engine;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaForgeEngineTests
{
    [Fact]
    public async Task LoadProjectAsync_rejects_invalid_project()
    {
        await using var engine = CreateEngine();
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new()
                {
                    Name = "Empty size",
                    Size = default
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.LoadProjectAsync(project));
    }

    [Fact]
    public async Task ApplyProjectUpdateAsync_runs_editor_validation()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        await engine.LoadProjectAsync(project);

        var canvasId = project.Canvases[0].Id;
        await engine.ApplyProjectUpdateAsync(e =>
        {
            e.AddText(canvasId, "Live", new Transform2D { Size = new CanvasSize(200, 64) });
        });

        Assert.Contains(engine.CurrentProject.Canvases[0].Objects, o => o.Name == "Text");
    }

    [Fact]
    public async Task BindOutputAsync_requires_matching_type()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var output = project.Outputs[0];
        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(output.Id, new OffscreenRenderOutputTarget()));
    }

    [Fact]
    public async Task Engine_start_creates_runtime_and_render_thread()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());

        await engine.StartAsync();

        Assert.True(engine.IsRunning);
        Assert.Equal(1, backendFactory.CreateAttempts);
        Assert.NotNull(engine.RuntimeForTests);
        Assert.NotNull(engine.RenderThreadForTests);
        Assert.True(engine.RenderThreadForTests!.IsRunning);
    }

    [Fact]
    public async Task Engine_start_registers_source_providers()
    {
        var providerFactory = new FakeMediaSourceProviderFactory();
        await using var engine = CreateEngine(providerFactory: providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());

        await engine.StartAsync();

        Assert.Single(providerFactory.Sources);
        Assert.Equal(1, providerFactory.CreateCount);
        Assert.Equal(
            MediaForge.Core.Sources.MediaSourceState.Running,
            providerFactory.Sources.Values.First().State);
    }

    [Fact]
    public async Task Engine_start_failure_rolls_back_started_sources()
    {
        var providerFactory = new FakeMediaSourceProviderFactory { FailAfterCount = 1 };
        var project = CreateValidProject();
        project.SourceDefinitions.Add(new MediaForgeSourceDefinition
        {
            Name = "Second",
            TypeId = MediaSourceTypes.Desktop,
            Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings())
        });

        await using var engine = CreateEngine(providerFactory: providerFactory);
        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());
        Assert.False(engine.IsRunning);
        Assert.All(providerFactory.Sources.Values, s =>
            Assert.Equal(MediaForge.Core.Sources.MediaSourceState.Stopped, s.State));
    }

    [Fact]
    public async Task Engine_stop_stops_sources_before_render_thread_shutdown()
    {
        var providerFactory = new FakeMediaSourceProviderFactory();
        await using var engine = CreateEngine(providerFactory: providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await engine.StopAsync();

        Assert.False(engine.IsRunning);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
        Assert.Equal(MediaForge.Core.Sources.MediaSourceState.Stopped, providerFactory.Sources.Values.First().State);
    }

    [Fact]
    public async Task Engine_bind_output_stores_sink_and_enqueues_binding_when_running()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        var project = CreateValidProject();
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();

        await engine.BindOutputAsync(
            project.Outputs[0].Id,
            new WinFormsPreviewRenderOutputTarget { WindowHandle = 123 });

        await WaitUntilAsync(
            () => backendFactory.Backend?.Bindings.ContainsKey(project.Outputs[0].Id) == true,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Engine_rejects_project_update_while_starting_or_stopping()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await engine.StopAsync();

        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task Engine_publish_frame_keeps_snapshot_lease_alive_until_render_thread_consumes_submission()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var source = providerFactory.Sources.Values.First();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.ActiveSlotRetainCount);

        backendFactory.Backend!.CompleteAllPending();
        await WaitUntilAsync(() => source.ActiveSlotRetainCount == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Engine_stop_disposes_render_backend()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await engine.StopAsync();

        Assert.True(backendFactory.Backend!.Disposed);
    }

    [Fact]
    public async Task Engine_start_failure_disposes_backend()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        var providerFactory = new FakeMediaSourceProviderFactory { FailAfterCount = 1 };
        var project = CreateValidProject();
        project.SourceDefinitions.Add(new MediaForgeSourceDefinition
        {
            Name = "Second",
            TypeId = MediaSourceTypes.Desktop,
            Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings())
        });

        await using var engine = CreateEngine(providerFactory, backendFactory);
        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());

        Assert.True(backendFactory.Backend!.Disposed);
    }

    [Fact]
    public async Task Engine_stop_attempts_backend_dispose_even_when_render_thread_dispose_fails()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        var engine = new MediaForgeEngine(
            providerFactory,
            new FakeRenderOutputSinkFactory(),
            backendFactory)
        {
            RenderThreadSubmissionShutdownTimeoutForTests = TimeSpan.FromMilliseconds(50)
        };

        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var ex = await Assert.ThrowsAsync<AggregateException>(() => engine.StopAsync());
        Assert.NotEmpty(ex.InnerExceptions);
        Assert.True(backendFactory.Backend!.Disposed);

        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Engine_stop_aggregates_cleanup_errors_after_attempting_all_cleanup()
    {
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        var backendFactory = new ThrowingDisposeRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var ex = await Assert.ThrowsAsync<AggregateException>(() => engine.StopAsync());

        Assert.True(ex.InnerExceptions.Count >= 2);
        Assert.Contains(
            ex.InnerExceptions,
            inner => inner.Message.Contains("Simulated provider stop failure", StringComparison.Ordinal));
        Assert.True(backendFactory.Backend!.DisposeAttempted);
    }

    private static MediaForgeEngine CreateEngine(
        IMediaSourceProviderFactory? providerFactory = null,
        IRenderBackendFactory? backendFactory = null)
    {
        return new MediaForgeEngine(
            providerFactory ?? new FakeMediaSourceProviderFactory(),
            new FakeRenderOutputSinkFactory(),
            backendFactory ?? new RecordingRenderBackendFactory());
    }

    private static MediaForgeProject CreateValidProject()
    {
        var editor = new MediaForgeProjectEditor(new());
        var source = editor.CreateSource("Desktop", new DesktopCaptureSourceSettings());
        var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
        editor.AddSourceLayer(
            canvas.Id,
            source.Id,
            new Transform2D { Size = new CanvasSize(1920, 1080) });
        var output = editor.CreateOutput(
            "Preview",
            canvas.Id,
            new PreviewWindowOutputSettings(),
            new FrameSize(1280, 720));
        editor.ValidateOrThrow();
        return editor.Project;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within the expected timeout.");
    }
}
