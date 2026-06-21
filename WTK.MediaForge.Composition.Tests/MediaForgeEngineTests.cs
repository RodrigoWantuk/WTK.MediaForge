using WTK.MediaForge.Composition;
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
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaForgeEngineTests
{
    [Fact]
    public async Task Engine_state_transitions_idle_loaded_running_idle()
    {
        await using var engine = CreateEngine();

        Assert.Equal(MediaForgeEngineState.Idle, engine.State);

        await engine.LoadProjectAsync(CreateValidProject());
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);

        await engine.StartAsync();
        Assert.Equal(MediaForgeEngineState.Running, engine.State);

        await engine.StopAsync();
        Assert.Equal(MediaForgeEngineState.Idle, engine.State);
    }

    [Fact]
    public async Task Engine_raises_StateChanged_on_load_start_stop()
    {
        var states = new List<MediaForgeEngineState>();
        await using var engine = CreateEngine();
        engine.StateChanged += (_, args) => states.Add(args.NewState);

        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await engine.StopAsync();

        Assert.Contains(MediaForgeEngineState.Loaded, states);
        Assert.Contains(MediaForgeEngineState.Starting, states);
        Assert.Contains(MediaForgeEngineState.Running, states);
        Assert.Contains(MediaForgeEngineState.Stopping, states);
        Assert.Contains(MediaForgeEngineState.Idle, states);
    }

    [Fact]
    public void MediaForgeEngine_does_not_expose_public_constructor()
    {
        Assert.Empty(typeof(MediaForgeEngine).GetConstructors());
    }

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

        var ex = await Assert.ThrowsAsync<MediaForgeProjectValidationException>(() =>
            engine.LoadProjectAsync(project));
        Assert.Contains(ex.ValidationResult.Issues, issue => issue.Code == "canvas.size.invalid");
    }

    [Fact]
    public async Task LoadProjectAsync_does_not_retain_external_project_reference()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();

        await engine.LoadProjectAsync(project);

        Assert.NotSame(project, engine.CurrentProject);
        Assert.NotSame(project.Canvases[0], engine.CurrentProject.Canvases[0]);
        Assert.NotSame(project.Outputs[0], engine.CurrentProject.Outputs[0]);
    }

    [Fact]
    public async Task LoadProjectAsync_migration_does_not_mutate_caller_project()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        project.SourceDefinitions[0].TypeId = LegacyMediaSourceTypeIds.DesktopCapture;

        await engine.LoadProjectAsync(project);

        Assert.Equal(LegacyMediaSourceTypeIds.DesktopCapture, project.SourceDefinitions[0].TypeId);
        Assert.Equal(MediaSourceTypes.Desktop, engine.CurrentProject.SourceDefinitions[0].TypeId);
    }

    [Fact]
    public async Task External_project_mutation_after_load_does_not_change_engine_project()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();

        await engine.LoadProjectAsync(project);
        project.Canvases[0].Name = "Mutated outside engine";
        project.Outputs[0].Name = "Mutated output";

        Assert.NotEqual(project.Canvases[0].Name, engine.CurrentProject.Canvases[0].Name);
        Assert.NotEqual(project.Outputs[0].Name, engine.CurrentProject.Outputs[0].Name);
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
    public async Task ApplyProjectUpdate_invalid_update_does_not_mutate_CurrentProject()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        await engine.LoadProjectAsync(project);
        var output = engine.CurrentProject.Outputs[0];
        var originalCanvasId = output.CanvasId;

        await Assert.ThrowsAsync<MediaForgeProjectValidationException>(() =>
            engine.ApplyProjectUpdateAsync(e => e.Project.Outputs[0].CanvasId = CanvasId.New()));

        Assert.Same(output, engine.CurrentProject.Outputs[0]);
        Assert.Equal(originalCanvasId, engine.CurrentProject.Outputs[0].CanvasId);
    }

    [Fact]
    public async Task ApplyProjectUpdate_invalid_update_does_not_replace_ProjectStateSnapshot()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        var projectState = engine.ProjectStateForTests;

        await Assert.ThrowsAsync<MediaForgeProjectValidationException>(() =>
            engine.ApplyProjectUpdateAsync(e => e.Project.Outputs[0].CanvasId = CanvasId.New()));

        Assert.Same(projectState, engine.ProjectStateForTests);
    }

    [Fact]
    public async Task ApplyProjectUpdate_invalid_update_does_not_publish_frame()
    {
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        var submitCount = backendFactory.Backend!.SubmitCount;

        await Assert.ThrowsAsync<MediaForgeProjectValidationException>(() =>
            engine.ApplyProjectUpdateAsync(e => e.Project.Outputs[0].CanvasId = CanvasId.New()));

        await Task.Delay(100);
        Assert.Equal(submitCount, backendFactory.Backend.SubmitCount);
        backendFactory.Backend.CompleteAllPending();
    }

    [Fact]
    public async Task ApplyProjectUpdate_valid_update_replaces_project_and_publishes_frame_when_running()
    {
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        var originalProject = engine.CurrentProject;
        var submitCount = backendFactory.Backend!.SubmitCount;
        var canvasId = engine.CurrentProject.Canvases[0].Id;

        await engine.ApplyProjectUpdateAsync(e =>
            e.AddText(canvasId, "Live", new Transform2D { Size = new CanvasSize(200, 64) }));

        Assert.NotSame(originalProject, engine.CurrentProject);
        Assert.Contains(engine.CurrentProject.Canvases[0].Objects, o => o.Name == "Text");
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount > submitCount, TimeSpan.FromSeconds(5));
        backendFactory.Backend.CompleteAllPending();
    }

    [Fact]
    public async Task BindOutputAsync_requires_matching_type()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var output = project.Outputs[0];
        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(output.Id, new OffscreenRenderOutputTarget()));
    }

    [Fact]
    public async Task BindOutput_unsupported_output_factory_throws_feature_exception()
    {
        await using var engine = CreateEngine(outputSinkFactory: new RejectingRenderOutputSinkFactory());
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);

        var ex = await Assert.ThrowsAsync<MediaForgeUnsupportedFeatureException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(1)));

        Assert.Equal($"output.{project.Outputs[0].TypeId.Value}", ex.FeatureCode);
    }

    [Fact]
    public async Task BindOutput_CreateSink_failure_keeps_existing_sink()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        sinkFactory.Enqueue(oldSink);
        await using var engine = CreateEngine(outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        sinkFactory.ThrowOnCreateSink = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
    }

    [Fact]
    public async Task BindOutput_CreateBinding_failure_keeps_existing_sink()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        var newSink = new RecordingRenderOutputSink(CreatePreviewTarget(2), "new")
        {
            ThrowOnCreateBinding = true
        };
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = CreateEngine(outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
        Assert.Equal(1, newSink.DisposeCount);
    }

    [Fact]
    public async Task BindOutput_EnqueueCommand_failure_disposes_new_sink_and_keeps_old_sink()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        var newSink = new RecordingRenderOutputSink(CreatePreviewTarget(2), "new");
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = CreateEngine(outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        engine.RenderThreadForTests!.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
        Assert.Equal(1, newSink.DisposeCount);
    }

    [Fact]
    public async Task BindOutput_backend_failure_keeps_existing_sink_and_throws_to_caller()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        var newSink = new RecordingRenderOutputSink(CreatePreviewTarget(2), "new");
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory,
            diagnostics: diagnostics);
        var reportedDiagnostics = new List<MediaForgeDiagnostic>();
        engine.DiagnosticReported += (_, args) => reportedDiagnostics.Add(args.Diagnostic);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        backendFactory.Backend!.ThrowOnBind = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

        Assert.Contains("Configured bind command failure", ex.Message, StringComparison.Ordinal);
        Assert.Same(oldSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, oldSink.DisposeCount);
        Assert.Equal(1, newSink.DisposeCount);
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "render.command_failed");
        Assert.Contains(reportedDiagnostics, d => d.Code == "render.command_failed");
    }

    [Fact]
    public async Task Engine_raises_DiagnosticReported_for_render_command_failure()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var diagnostics = new List<MediaForgeDiagnostic>();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.DiagnosticReported += (_, args) => diagnostics.Add(args.Diagnostic);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();

        backendFactory.Backend!.ThrowOnBind = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(1)));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "render.command_failed");
    }

    [Fact]
    public async Task BindOutput_success_disposes_old_sink_after_new_binding_is_accepted()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        var newSink = new RecordingRenderOutputSink(CreatePreviewTarget(2), "new");
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = CreateEngine(outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        await engine.BindOutputAsync(outputId, CreatePreviewTarget(2));

        Assert.Same(newSink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(1, oldSink.DisposeCount);
        Assert.Equal(0, newSink.DisposeCount);
    }

    [Fact]
    public async Task UnbindOutput_enqueues_unbind_before_disposing_sink()
    {
        var backendFactory = new UnbindTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(
            CreatePreviewTarget(1),
            "old",
            waitBeforeDispose: () => backendFactory.Backend!.WaitForUnbind(TimeSpan.FromSeconds(5)));
        sinkFactory.Enqueue(sink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        await engine.UnbindOutputAsync(outputId);

        Assert.Equal(1, sink.DisposeCount);
        Assert.Equal(1, backendFactory.Backend!.UnbindCount);
        Assert.Null(engine.GetOutputSinkForTests(outputId));
    }

    [Fact]
    public async Task UnbindOutput_backend_failure_keeps_output_registered()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        sinkFactory.Enqueue(sink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        backendFactory.Backend!.ThrowOnUnbind = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.UnbindOutputAsync(outputId));

        Assert.Contains("Configured unbind command failure", ex.Message, StringComparison.Ordinal);
        Assert.Same(sink, engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, sink.DisposeCount);
    }

    [Fact]
    public async Task UnbindOutput_dispose_failure_does_not_leave_output_registered()
    {
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old")
        {
            ThrowOnDispose = true
        };
        sinkFactory.Enqueue(sink);
        await using var engine = CreateEngine(outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.UnbindOutputAsync(outputId));

        Assert.Null(engine.GetOutputSinkForTests(outputId));
        Assert.Equal(0, engine.OutputSinkCountForTests);
    }

    [Fact]
    public async Task UnbindOutput_missing_output_is_noop()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());

        await engine.UnbindOutputAsync(RenderOutputId.New());

        Assert.Equal(0, engine.OutputSinkCountForTests);
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
    public async Task StartAsync_prebound_output_backend_bind_failure_rolls_back_start()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory(throwOnBind: true);
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "prebound");
        sinkFactory.Enqueue(sink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());

        Assert.False(engine.IsRunning);
        Assert.Null(engine.RenderThreadForTests);
        Assert.True(backendFactory.Backend!.Disposed);
        Assert.Same(sink, engine.GetOutputSinkForTests(outputId));
    }

    [Fact]
    public async Task RenderThread_command_completion_is_observed_by_engine()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(1), "blocked"));
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        backendFactory.Backend!.ResetBindRelease();

        var bindTask = engine.BindOutputAsync(outputId, CreatePreviewTarget(1));

        Assert.True(backendFactory.Backend.WaitForBindEntered(TimeSpan.FromSeconds(5)));
        Assert.False(bindTask.IsCompleted);

        backendFactory.Backend.ReleaseBind();
        await bindTask;

        Assert.Equal(1, backendFactory.Backend.BindCount);
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
    public async Task Engine_raises_FrameDropped_when_pending_tracker_is_full()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        var frameDropped = new List<MediaForgeFrameDroppedEventArgs>();
        await engine.LoadProjectAsync(CreateValidProject());
        var canvasId = engine.CurrentProject.Canvases[0].Id;
        engine.FrameDropped += (_, args) => frameDropped.Add(args);

        try
        {
            await engine.StartAsync();
            await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));

            await engine.ApplyProjectUpdateAsync(editor =>
                editor.AddText(canvasId, "A", new Transform2D { Size = new CanvasSize(200, 64) }));
            await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 2, TimeSpan.FromSeconds(5));

            await engine.ApplyProjectUpdateAsync(editor =>
                editor.AddText(canvasId, "B", new Transform2D { Size = new CanvasSize(200, 64) }));

            await WaitUntilAsync(() => frameDropped.Count > 0, TimeSpan.FromSeconds(5));
            Assert.Contains(frameDropped, args => args.ReasonCode == "render.frame_dropped_tracker_full");
        }
        finally
        {
            backendFactory.Backend?.CompleteAllPending();
        }
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
    public async Task Engine_stop_does_not_dispose_backend_when_render_thread_is_still_alive()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new BlockingSubmitRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory);
        engine.RenderThreadJoinTimeoutForTests = TimeSpan.FromMilliseconds(50);

        try
        {
            await engine.LoadProjectAsync(CreateValidProject());
            await engine.StartAsync();
            Assert.True(backendFactory.Backend!.WaitForSubmitEntered(TimeSpan.FromSeconds(5)));

            var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
            var aggregate = Assert.IsType<AggregateException>(ex.InnerException);

            Assert.False(backendFactory.Backend.Disposed);
            Assert.Contains(
                aggregate.InnerExceptions,
                inner => inner.Message.Contains("render thread is still alive", StringComparison.Ordinal));
        }
        finally
        {
            backendFactory.Backend?.ReleaseSubmit();
            _ = backendFactory.Backend?.WaitForSubmitExited(TimeSpan.FromSeconds(5));
            backendFactory.Backend?.Dispose();
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Engine_stop_reports_fatal_when_backend_dispose_is_skipped_due_to_live_render_thread()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new BlockingSubmitRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory, diagnostics: diagnostics);
        engine.RenderThreadJoinTimeoutForTests = TimeSpan.FromMilliseconds(50);

        try
        {
            await engine.LoadProjectAsync(CreateValidProject());
            await engine.StartAsync();
            Assert.True(backendFactory.Backend!.WaitForSubmitEntered(TimeSpan.FromSeconds(5)));

            await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

            Assert.Contains(
                diagnostics.Diagnostics,
                d => d.Severity == MediaForgeDiagnosticSeverity.Fatal &&
                    d.Code == "engine.backend_dispose_skipped_render_thread_alive");
        }
        finally
        {
            backendFactory.Backend?.ReleaseSubmit();
            _ = backendFactory.Backend?.WaitForSubmitExited(TimeSpan.FromSeconds(5));
            backendFactory.Backend?.Dispose();
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Engine_failed_stop_sets_state_failed()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new BlockingSubmitRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory);
        engine.RenderThreadJoinTimeoutForTests = TimeSpan.FromMilliseconds(50);

        try
        {
            await engine.LoadProjectAsync(CreateValidProject());
            await engine.StartAsync();
            Assert.True(backendFactory.Backend!.WaitForSubmitEntered(TimeSpan.FromSeconds(5)));

            await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
            Assert.NotNull(engine.RenderThreadForTests);
            Assert.NotNull(engine.BackendForTests);
        }
        finally
        {
            backendFactory.Backend?.ReleaseSubmit();
            _ = backendFactory.Backend?.WaitForSubmitExited(TimeSpan.FromSeconds(5));
            backendFactory.Backend?.Dispose();
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Engine_cleanup_error_sets_state_failed()
    {
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        await using var engine = CreateEngine(providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
    }

    [Fact]
    public async Task Engine_dispose_after_failed_stop_preserves_failed_state()
    {
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        var engine = CreateEngine(providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
    }

    [Fact]
    public async Task Engine_start_after_failed_state_throws_clear_exception()
    {
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        await using var engine = CreateEngine(providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.Equal(MediaForgeEngineState.Failed, ex.EngineState);
        Assert.Contains("failed state", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task Engine_stop_disposes_backend_when_render_thread_cleanup_fails_but_thread_stopped()
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

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
        var aggregate = Assert.IsType<AggregateException>(ex.InnerException);
        Assert.NotEmpty(aggregate.InnerExceptions);
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

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
        var aggregate = Assert.IsType<AggregateException>(ex.InnerException);

        Assert.True(aggregate.InnerExceptions.Count >= 2);
        Assert.Contains(
            aggregate.InnerExceptions,
            inner => inner.Message.Contains("Simulated provider stop failure", StringComparison.Ordinal));
        Assert.True(backendFactory.Backend!.DisposeAttempted);
    }

    private static MediaForgeEngine CreateEngine(
        IMediaSourceProviderFactory? providerFactory = null,
        IRenderBackendFactory? backendFactory = null,
        IRenderOutputSinkFactory? outputSinkFactory = null,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        return new MediaForgeEngine(
            providerFactory ?? new FakeMediaSourceProviderFactory(),
            outputSinkFactory ?? new FakeRenderOutputSinkFactory(),
            backendFactory ?? new RecordingRenderBackendFactory(),
            diagnostics);
    }

    private static WinFormsPreviewRenderOutputTarget CreatePreviewTarget(nint handle) =>
        new() { WindowHandle = handle };

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
