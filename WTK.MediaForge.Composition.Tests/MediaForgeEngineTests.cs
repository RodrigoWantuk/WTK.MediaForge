using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
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

    private static MediaForgeEngine CreateEngine(
        FakeMediaSourceProviderFactory? providerFactory = null,
        RecordingRenderBackendFactory? backendFactory = null)
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
