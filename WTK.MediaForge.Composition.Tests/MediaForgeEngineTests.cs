using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Recovery;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Scenes.Editing;
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
    public async Task Runtime_health_snapshot_tracks_engine_and_recovery_state()
    {
        await using var engine = CreateEngine();
        Assert.Equal(MediaForgeRuntimeHealthStatus.Stopped, engine.GetRuntimeHealthSnapshot().Status);

        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        Assert.Equal(MediaForgeRuntimeHealthStatus.Healthy, engine.GetRuntimeHealthSnapshot().Status);

        var observed = new TaskCompletionSource<MediaForgeRecoverySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.RecoveryStateChanged += (_, args) => observed.TrySetResult(args.Recovery);
        engine.FaultRecoveryCoordinatorForTests!.NotifySourceProviderFailed("Webcam removed.");

        var recovery = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var health = engine.GetRuntimeHealthSnapshot();
        Assert.Equal(MediaForgeRecoveryArea.Source, recovery.Area);
        Assert.Equal(MediaForgeRuntimeHealthStatus.Recovering, health.Status);
        Assert.Single(health.Recoveries);
        Assert.True(health.Recoveries[0].IsolatesSource);
    }

    [Fact]
    public async Task Render_submit_failure_recreates_backend_and_restores_running_engine()
    {
        var backendFactory = new RecoveringRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 30;
        var recovered = new TaskCompletionSource<MediaForgeRecoverySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.RecoveryStateChanged += (_, args) =>
        {
            if (args.Recovery is
                {
                    Area: MediaForgeRecoveryArea.GraphicsDevice,
                    Status: MediaForgeRecoveryStatus.Recovered
                })
            {
                recovered.TrySetResult(args.Recovery);
            }
        };

        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var recovery = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () => (backendFactory.ReplacementBackend?.RenderCount ?? 0) > 0,
            TimeSpan.FromSeconds(5));

        Assert.Equal("graphics-device", recovery.ResourceId);
        Assert.Equal(MediaForgeEngineState.Running, engine.State);
        Assert.Equal(2, backendFactory.CreateAttempts);
        Assert.True(Assert.IsType<SubmitFailingRenderBackend>(backendFactory.CreatedBackends[0]).Disposed);
        Assert.Same(backendFactory.ReplacementBackend, engine.BackendForTests);
    }

    [Fact]
    public async Task Engine_state_transitions_idle_loaded_running_loaded()
    {
        await using var engine = CreateEngine();

        Assert.Equal(MediaForgeEngineState.Idle, engine.State);

        await engine.LoadProjectAsync(CreateValidProject());
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);

        await engine.StartAsync();
        Assert.Equal(MediaForgeEngineState.Running, engine.State);

        await engine.StopAsync();
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
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
        Assert.Contains(MediaForgeEngineState.Loaded, states);
    }

    [Fact]
    public void MediaForgeEngine_does_not_expose_public_constructor()
    {
        Assert.Empty(typeof(MediaForgeEngine).GetConstructors());
    }

    [Fact]
    public async Task StartAsync_without_loaded_project_throws()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.Equal(MediaForgeEngineState.Idle, ex.EngineState);
        Assert.Contains("project", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopAsync_after_running_returns_to_Loaded()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await engine.StopAsync();

        Assert.True(engine.HasProject);
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
    }

    [Fact]
    public async Task LoadProject_from_Loaded_replaces_project()
    {
        await using var engine = CreateEngine();
        var first = CreateValidProject();
        var second = CreateValidProject();
        second.Canvases[0].Name = "Replacement";

        await engine.LoadProjectAsync(first);
        await engine.LoadProjectAsync(second);

        Assert.Equal("Replacement", engine.CurrentProject!.Canvases[0].Name);
        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
    }

    [Fact]
    public async Task LoadProject_while_Running_throws()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.LoadProjectAsync(CreateValidProject()));
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
        var currentProject = GetCurrentProject(engine);

        Assert.NotSame(project, currentProject);
        Assert.NotSame(project.Canvases[0], currentProject.Canvases[0]);
        Assert.NotSame(project.Outputs[0], currentProject.Outputs[0]);
    }

    [Fact]
    public async Task CurrentProject_returns_copy_not_engine_owned_instance()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());

        var first = engine.CurrentProject;
        var second = engine.CurrentProject;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.NotSame(first!.Canvases[0], second!.Canvases[0]);
    }

    [Fact]
    public async Task Mutating_CurrentProject_copy_does_not_mutate_engine_project()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());

        var copy = engine.CurrentProject!;
        copy.Outputs.Clear();
        copy.Canvases[0].Objects.Clear();
        copy.Canvases[0].Size = default;

        var freshCopy = engine.CurrentProject!;
        Assert.NotEmpty(freshCopy.Outputs);
        Assert.NotEmpty(freshCopy.Canvases[0].Objects);
        Assert.False(freshCopy.Canvases[0].Size.IsEmpty);
    }

    [Fact]
    public async Task ApplyProjectUpdate_is_only_supported_mutation_path()
    {
        await using var engine = CreateEngine();
        await engine.LoadProjectAsync(CreateValidProject());
        var canvasId = engine.CurrentProject!.Canvases[0].Id;

        engine.CurrentProject!.Canvases[0].Objects.Clear();
        Assert.NotEmpty(engine.CurrentProject!.Canvases[0].Objects);

        await engine.ApplyProjectUpdateAsync(editor =>
            editor.AddText(canvasId, "Updated", new Transform2D { Size = new CanvasSize(100, 40) }));

        Assert.Contains(engine.CurrentProject!.Canvases[0].Objects, item => item.Name == "Text");
    }

    [Fact]
    public async Task Live_scene_edit_mutates_published_scene_immediately()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Live);

        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerVisibility(layerId, false));

        var current = GetCurrentProject(engine);
        Assert.False(current.Canvases[0].Objects[0].Enabled);
        Assert.NotEqual(
            session.BasePublishedVersionId,
            engine.SceneRuntimeForTests!.PublishedStates[canvasId].VersionId);
    }

    [Fact]
    public async Task Apply_scene_edit_keeps_published_scene_until_commit()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Apply);

        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerVisibility(layerId, false));

        Assert.True(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
        Assert.True(engine.SceneRuntimeForTests!.TryGetDraft(session.SessionId, out var draft));
        Assert.True(draft!.HasChanges);

        var result = await engine.ApplySceneDraftAsync(session.SessionId, new SceneCommitRequest());

        Assert.False(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
        Assert.Equal(canvasId, result.CanvasId);
        Assert.NotEqual(result.OldVersionId, result.NewVersionId);
        Assert.Contains(canvasId, result.AffectedCanvases);
    }

    [Fact]
    public async Task Apply_scene_mutation_batch_is_atomic_for_apply_session()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Apply);

        await engine.ApplySceneMutationsAsync(
            session.SessionId,
            [
                new SceneMutationPatch.SetLayerVisibility(layerId, false),
                new SceneMutationPatch.SetLayerOpacity(layerId, 0.4f)
            ]);

        Assert.True(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
        Assert.True(engine.SceneRuntimeForTests!.TryGetDraft(session.SessionId, out var draft));
        Assert.True(draft!.HasChanges);

        await engine.ApplySceneDraftAsync(session.SessionId, new SceneCommitRequest());
        var publishedLayer = GetCurrentProject(engine).Canvases[0].Objects[0];
        Assert.False(publishedLayer.Enabled);
        Assert.Equal(0.4f, publishedLayer.Opacity);
    }

    [Fact]
    public async Task Apply_scene_mutation_batch_preserves_state_when_any_patch_is_invalid()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Live);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await engine.ApplySceneMutationsAsync(
                session.SessionId,
                [
                    new SceneMutationPatch.SetLayerVisibility(layerId, false),
                    new SceneMutationPatch.SetLayerOpacity(layerId, 2f)
                ]));

        Assert.True(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
    }

    [Fact]
    public async Task Apply_scene_draft_accepts_explicit_cut_policy()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Apply);
        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerVisibility(layerId, false));

        var result = await engine.ApplySceneDraftAsync(
            session.SessionId,
            new SceneCommitRequest { TransitionPolicy = SceneApplyTransitionPolicy.Cut() });

        Assert.False(result.TransitionRequested);
        Assert.False(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
    }

    [Fact]
    public async Task Discard_scene_draft_does_not_mutate_published_scene()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        var canvasId = project.Canvases[0].Id;
        var layerId = project.Canvases[0].Objects[0].Id;

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(canvasId, SceneEditMode.Apply);
        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerVisibility(layerId, false));

        await engine.DiscardSceneDraftAsync(session.SessionId);

        Assert.True(GetCurrentProject(engine).Canvases[0].Objects[0].Enabled);
        Assert.False(engine.SceneRuntimeForTests!.TryGetDraft(session.SessionId, out _));
    }

    [Fact]
    public async Task Apply_nested_scene_edit_reports_parent_scene_output_as_affected()
    {
        await using var engine = CreateEngine();
        var project = CreateNestedSceneProject();
        var childCanvas = project.Canvases.Single(canvas => canvas.Name == "Child");
        var childLayerId = childCanvas.Objects[0].Id;
        var parentCanvas = project.Canvases.Single(canvas => canvas.Name == "Parent");
        var parentOutput = project.Outputs.Single(output => output.CanvasId == parentCanvas.Id);

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(childCanvas.Id, SceneEditMode.Apply);
        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerOpacity(childLayerId, 0.5f));

        var result = await engine.ApplySceneDraftAsync(
            session.SessionId,
            new SceneCommitRequest
            {
                TransitionPolicy = SceneApplyTransitionPolicy.Fade(TimeSpan.FromMilliseconds(300))
            });

        Assert.Contains(childCanvas.Id, result.AffectedCanvases);
        Assert.Contains(parentCanvas.Id, result.AffectedCanvases);
        Assert.Contains(parentOutput.Id, result.AffectedOutputs);
        Assert.True(result.TransitionRequested);
        Assert.True(engine.OutputRouteTransitionRuntimeForTests.TryGetTransition(parentOutput.Id, out var transition));
        Assert.Equal(parentCanvas.Id, transition.CurrentVersionGraph.RootCanvasId);
        Assert.Equal(result.OldVersionId, transition.PreviousVersionGraph.CanvasVersions[childCanvas.Id]);
        Assert.Equal(result.NewVersionId, transition.CurrentVersionGraph.CanvasVersions[childCanvas.Id]);
        Assert.NotNull(transition.PreviousProjectState);
    }

    [Fact]
    public async Task Apply_nested_scene_effect_parameter_edit_reports_parent_output_as_affected()
    {
        await using var engine = CreateEngine();
        var project = CreateNestedSceneProject();
        var childCanvas = project.Canvases.Single(canvas => canvas.Name == "Child");
        var childLayer = childCanvas.Objects[0];
        var effect = new BlurEffect { Radius = 4f, Order = 0 };
        childLayer.Effects.Add(effect);
        var parentCanvas = project.Canvases.Single(canvas => canvas.Name == "Parent");
        var parentOutput = project.Outputs.Single(output => output.CanvasId == parentCanvas.Id);

        await engine.LoadProjectAsync(project);
        var session = await engine.BeginSceneEditSessionAsync(childCanvas.Id, SceneEditMode.Apply);

        await engine.ApplySceneMutationAsync(
            session.SessionId,
            new SceneMutationPatch.SetLayerEffects(
                childLayer.Id,
                [
                    new BlurEffect
                    {
                        Id = effect.Id,
                        Radius = 12f,
                        Order = effect.Order
                    }
                ]));

        var result = await engine.ApplySceneDraftAsync(
            session.SessionId,
            new SceneCommitRequest
            {
                TransitionPolicy = SceneApplyTransitionPolicy.Fade(TimeSpan.FromMilliseconds(300))
            });

        Assert.NotEqual(result.OldVersionId, result.NewVersionId);
        Assert.Contains(childCanvas.Id, result.AffectedCanvases);
        Assert.Contains(parentCanvas.Id, result.AffectedCanvases);
        Assert.Contains(parentOutput.Id, result.AffectedOutputs);
        Assert.True(result.TransitionRequested);
        Assert.True(engine.OutputRouteTransitionRuntimeForTests.TryGetTransition(parentOutput.Id, out var transition));
        Assert.Equal(result.OldVersionId, transition.PreviousVersionGraph.CanvasVersions[childCanvas.Id]);
        Assert.Equal(result.NewVersionId, transition.CurrentVersionGraph.CanvasVersions[childCanvas.Id]);
    }

    [Fact]
    public async Task LoadProjectAsync_migration_does_not_mutate_caller_project()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        project.SourceDefinitions[0].TypeId = LegacyMediaSourceTypeIds.DesktopCapture;

        await engine.LoadProjectAsync(project);

        Assert.Equal(LegacyMediaSourceTypeIds.DesktopCapture, project.SourceDefinitions[0].TypeId);
        Assert.Equal(MediaSourceTypes.Desktop, GetCurrentProject(engine).SourceDefinitions[0].TypeId);
    }

    [Fact]
    public async Task External_project_mutation_after_load_does_not_change_engine_project()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();

        await engine.LoadProjectAsync(project);
        project.Canvases[0].Name = "Mutated outside engine";
        project.Outputs[0].Name = "Mutated output";
        var currentProject = GetCurrentProject(engine);

        Assert.NotEqual(project.Canvases[0].Name, currentProject.Canvases[0].Name);
        Assert.NotEqual(project.Outputs[0].Name, currentProject.Outputs[0].Name);
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

        Assert.Contains(GetCurrentProject(engine).Canvases[0].Objects, o => o.Name == "Text");
    }

    [Fact]
    public async Task ApplyProjectUpdate_invalid_update_does_not_mutate_CurrentProject()
    {
        await using var engine = CreateEngine();
        var project = CreateValidProject();
        await engine.LoadProjectAsync(project);
        var output = GetCurrentProject(engine).Outputs[0];
        var originalCanvasId = output.CanvasId;

        await Assert.ThrowsAsync<MediaForgeProjectValidationException>(() =>
            engine.ApplyProjectUpdateAsync(e => e.Project.Outputs[0].CanvasId = CanvasId.New()));

        Assert.Equal(output.Id, engine.CurrentProject!.Outputs[0].Id);
        Assert.Equal(originalCanvasId, GetCurrentProject(engine).Outputs[0].CanvasId);
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
        engine.RenderFramesPerSecond = 1;
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
        engine.RenderFramesPerSecond = 1;
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        var originalProject = engine.CurrentProject;
        var submitCount = backendFactory.Backend!.SubmitCount;
        var canvasId = GetCurrentProject(engine).Canvases[0].Id;

        await engine.ApplyProjectUpdateAsync(e =>
            e.AddText(canvasId, "Live", new Transform2D { Size = new CanvasSize(200, 64) }));

        Assert.NotSame(originalProject, engine.CurrentProject);
        Assert.Contains(GetCurrentProject(engine).Canvases[0].Objects, o => o.Name == "Text");
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount > submitCount, TimeSpan.FromSeconds(5));
        backendFactory.Backend.CompleteAllPending();
    }

    [Fact]
    public async Task Scheduled_frame_submits_render_graph_execution_with_acquired_source_frame()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        engine.RenderFramesPerSecond = 1;
        var project = CreateValidProject();
        var sourceId = project.SourceDefinitions[0].Id;

        await engine.LoadProjectAsync(project);
        await engine.StartAsync();

        await WaitUntilAsync(
            () => backendFactory.Backend!.LastRenderGraphExecution is { ExecutedNodeKeys.Count: > 0 },
            TimeSpan.FromSeconds(5));

        try
        {
            var execution = backendFactory.Backend!.LastRenderGraphExecution;
            Assert.NotNull(execution);

            Assert.Contains(
                execution!.ExecutedNodeKeys,
                key => key.StartsWith($"source:{sourceId}:", StringComparison.Ordinal));
            var outputKey = Assert.Single(
                execution.ExecutedNodeKeys,
                key => key.StartsWith("output:", StringComparison.Ordinal));

            var outputResult = execution.NodeResults[outputKey];
            Assert.False(outputResult.WasSkipped);
            Assert.Equal(sourceId, outputResult.SourceFrame?.SourceId);
        }
        finally
        {
            backendFactory.Backend!.CompleteAllPending();
        }
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
    public async Task RenderOutput_can_attach_frame_notification_sink()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var sink = new FrameNotificationSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);

        Assert.True(engine.IsSinkAttachedForTests(project.Outputs[0].Id, sink.Id));
        Assert.Equal(1, engine.OutputSinkCountForTests);
    }

    [Fact]
    public async Task RenderOutput_can_attach_multiple_sinks()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var first = new FrameNotificationSink();
        var second = new FrameNotificationSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, first);
        await engine.AttachSinkAsync(project.Outputs[0].Id, second);

        Assert.Equal(2, engine.AttachedSinkCountForTests);
        Assert.Equal(1, engine.OutputSinkCountForTests);
    }

    [Fact]
    public void BackpressureMode_does_not_expose_BlockProducer_until_supported()
    {
        Assert.DoesNotContain(
            "BlockProducer",
            Enum.GetNames<RenderOutputSinkBackpressureMode>());
    }

    [Fact]
    public async Task AttachSink_rejects_same_sink_instance_twice()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var sink = new RecordingPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AttachSinkAsync(project.Outputs[0].Id, sink));

        Assert.Equal(1, sink.StartCount);
        Assert.Equal(1, engine.AttachedSinkCountForTests);
    }

    [Fact]
    public async Task AttachSink_rejects_duplicate_sink_id_without_calling_StartAsync()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var sinkId = RenderOutputSinkId.New();
        var first = new RecordingPublicRenderOutputSink(sinkId);
        var duplicate = new RecordingPublicRenderOutputSink(sinkId);

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, first);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AttachSinkAsync(project.Outputs[0].Id, duplicate));

        Assert.Equal(0, duplicate.StartCount);
        Assert.Equal(0, duplicate.DisposeCount);
        Assert.Equal(1, engine.AttachedSinkCountForTests);
    }

    [Fact]
    public async Task Sink_receives_frame_number_timestamp_size_and_output_id()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var frameReady = new TaskCompletionSource<RenderOutputFrameInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new FrameNotificationSink(onFrame: (frame, _) =>
        {
            frameReady.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();

        var frame = await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(project.Outputs[0].Id, frame.OutputId);
        Assert.Equal(1, frame.FrameNumber);
        Assert.True(frame.Timestamp >= TimeSpan.Zero);
        Assert.Equal(project.Outputs[0].OutputSize, frame.Size);
        Assert.Equal(RenderPixelFormat.Rgba8Unorm, frame.Format);
        Assert.Equal(RenderBackendKind.Vulkan, frame.BackendKind);
    }

    [Fact]
    public async Task Sink_is_not_notified_before_submission_completion()
    {
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var sink = new RecordingPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(0, sink.FrameCount);

        backendFactory.Backend!.CompleteAllPending();
        await WaitUntilAsync(() => sink.FrameCount >= 1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sink_receives_frame_after_submission_completes()
    {
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var frameReady = new TaskCompletionSource<RenderOutputFrameInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingPublicRenderOutputSink(onFrame: (frame, _) =>
        {
            frameReady.TrySetResult(frame.Info);
            return ValueTask.CompletedTask;
        });

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));

        backendFactory.Backend!.CompleteAllPending();
        var frame = await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(project.Outputs[0].Id, frame.OutputId);
        Assert.Equal(sink.Id, frame.SinkId);
    }

    [Fact]
    public async Task Submission_resources_are_not_released_until_sink_frame_lease_is_released()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        engine.RenderFramesPerSecond = 0.1;
        var project = CreateOffscreenProject();
        var sink = new BlockingPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        var source = providerFactory.Sources.Values.First();
        Assert.Equal(1, source.ActiveSlotRetainCount);

        backendFactory.Backend!.CompleteAllPending();
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.ActiveSlotRetainCount);

        sink.Release();
        await WaitUntilAsync(() => source.ActiveSlotRetainCount == 1, TimeSpan.FromSeconds(5));
        backendFactory.Backend!.CompleteAllPending();
        await engine.StopAsync();
        Assert.Equal(0, source.ActiveSlotRetainCount);
    }

    [Fact]
    public async Task Dropped_sink_frame_releases_output_frame_lease()
    {
        var dispatcher = new RenderOutputSinkDispatcher();
        var project = CreateOffscreenProject();
        var output = project.Outputs[0];
        var sink = new BlockingPublicRenderOutputSink();
        var firstBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);
        var droppedBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);
        var latestBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);
        var replacingBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        try
        {
            dispatcher.PublishCompletedFrames(firstBatch);
            await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));
            dispatcher.PublishCompletedFrames(droppedBatch);
            Assert.True(droppedBatch.HasOutstandingLeases);

            dispatcher.PublishCompletedFrames(latestBatch);
            dispatcher.PublishCompletedFrames(replacingBatch);

            await WaitUntilAsync(() => !droppedBatch.HasOutstandingLeases, TimeSpan.FromSeconds(5));
        }
        finally
        {
            sink.Release();
            await dispatcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dropped_frame_dispose_failure_reports_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(diagnostics);
        var project = CreateOffscreenProject();
        var output = project.Outputs[0];
        var sink = new BlockingPublicRenderOutputSink();
        var firstBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);
        var droppedBatch = CreateRenderedOutputFrameBatch(
            output.Id,
            output.OutputSize,
            () => ValueTask.FromException(new InvalidOperationException("Configured dropped frame release failure.")));
        var latestBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);
        var replacingBatch = CreateRenderedOutputFrameBatch(output.Id, output.OutputSize);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        try
        {
            dispatcher.PublishCompletedFrames(firstBatch);
            await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));
            dispatcher.PublishCompletedFrames(droppedBatch);
            dispatcher.PublishCompletedFrames(latestBatch);
            dispatcher.PublishCompletedFrames(replacingBatch);

            await WaitUntilAsync(
                () => diagnostics.Diagnostics.Any(d => d.Code == "sink.dropped_frame_dispose_failed"),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            sink.Release();
            await dispatcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task Slow_preview_sink_does_not_block_render_thread()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 30;
        var project = CreateOffscreenProject();
        var sink = new BlockingPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        await WaitUntilAsync(() => backendFactory.Backend!.RenderCount >= 2, TimeSpan.FromSeconds(5));

        sink.Release();
    }

    [Fact]
    public async Task Sink_failure_reports_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        await using var engine = CreateEngine(diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var sink = new RecordingPublicRenderOutputSink { ThrowOnFrame = true };

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();

        await WaitUntilAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "sink.frame_delivery_failed"),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AttachSink_failure_rolls_back()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var sink = new RecordingPublicRenderOutputSink { ThrowOnStart = true };

        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AttachSinkAsync(project.Outputs[0].Id, sink));

        Assert.Equal(0, engine.AttachedSinkCountForTests);
        Assert.Equal(0, engine.OutputSinkCountForTests);
        Assert.Equal(1, sink.DisposeCount);
    }

    [Fact]
    public async Task AttachSink_timeout_cancels_attach_and_does_not_leave_sink_registered()
    {
        await using var engine = CreateEngine();
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
        var sink = new HangingStartPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.AttachSinkAsync(project.Outputs[0].Id, sink));

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
        await WaitUntilAsync(() => sink.StartCancellationObserved, TimeSpan.FromSeconds(5));
        Assert.False(engine.IsSinkAttachedForTests(project.Outputs[0].Id, sink.Id));
        Assert.Equal(0, engine.AttachedSinkCountForTests);
        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
    }

    [Fact]
    public async Task AttachSink_timeout_does_not_leave_automatic_surface_binding()
    {
        await using var engine = CreateEngine();
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
        var sink = new HangingStartPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.AttachSinkAsync(project.Outputs[0].Id, sink));

        Assert.Equal(0, engine.OutputSinkCountForTests);
    }

    [Fact]
    public async Task DetachSink_stops_delivery_and_disposes_sink()
    {
        await using var engine = CreateEngine();
        var project = CreateOffscreenProject();
        var sink = new RecordingPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id);

        Assert.False(engine.IsSinkAttachedForTests(project.Outputs[0].Id, sink.Id));
        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
        Assert.Equal(0, engine.OutputSinkCountForTests);
    }

    [Fact]
    public async Task DetachSink_timeout_sets_failed_and_does_not_return_success()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = CreateEngine(backendFactory: backendFactory, diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
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
            await WaitUntilAsync(() => sink.DisposeCount == 1, TimeSpan.FromSeconds(5));
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sink_worker_ignoring_cancellation_does_not_block_engine_dispose_forever()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = CreateEngine(backendFactory: backendFactory, diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
        var sink = new HungPublicRenderOutputSink();

        try
        {
            await engine.LoadProjectAsync(project);
            await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
            await engine.StartAsync();
            await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id));

            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
            sink.Release();
            await WaitUntilAsync(() => sink.DisposeCount == 1, TimeSpan.FromSeconds(5));

            var started = Environment.TickCount64;
            await engine.DisposeAsync();
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

            Assert.True(elapsed < TimeSpan.FromSeconds(5));
            Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
            Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");
        }
        finally
        {
            sink.Release();
        }
    }

    [Fact]
    public async Task Sink_worker_timeout_sets_engine_failed_on_detach()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = CreateEngine(backendFactory: backendFactory, diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
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
            Assert.False(engine.IsSinkAttachedForTests(project.Outputs[0].Id, sink.Id));
        }
        finally
        {
            sink.Release();
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sink_stop_timeout_does_not_block_engine_forever()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
        var sink = new HungPublicRenderOutputSink();

        try
        {
            await engine.LoadProjectAsync(project);
            await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
            await engine.StartAsync();
            await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

            var started = Environment.TickCount64;
            await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.DetachSinkAsync(project.Outputs[0].Id, sink.Id));
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

            Assert.True(elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            sink.Release();
            await WaitUntilAsync(() => sink.DisposeCount == 1, TimeSpan.FromSeconds(5));
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task One_RenderOutput_can_feed_two_sinks_without_rendering_twice()
    {
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(backendFactory: backendFactory);
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FrameNotificationSink(onFrame: (_, _) =>
        {
            firstReady.TrySetResult();
            return ValueTask.CompletedTask;
        });
        var second = new FrameNotificationSink(onFrame: (_, _) =>
        {
            secondReady.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, first);
        await engine.AttachSinkAsync(project.Outputs[0].Id, second);
        await engine.StartAsync();

        await Task.WhenAll(
            firstReady.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            secondReady.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, backendFactory.Backend!.RenderCount);
    }

    [Fact]
    public async Task FrameNotificationSink_receives_at_least_one_completed_frame_from_sample_project()
    {
        await using var engine = CreateEngine();
        engine.RenderFramesPerSecond = 1;
        var project = CreateOffscreenProject();
        var frameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new FrameNotificationSink(onFrame: (_, _) =>
        {
            frameReady.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();

        await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
    public async Task StartAsync_provider_hang_times_out_and_rolls_back()
    {
        var providerFactory = new HangingStartMediaSourceProviderFactory();
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        engine.StartTimeout = TimeSpan.FromMilliseconds(50);
        engine.StopTimeout = TimeSpan.FromSeconds(1);
        await engine.LoadProjectAsync(CreateValidProject());

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
        Assert.Null(engine.RenderThreadForTests);
        Assert.True(backendFactory.Backend!.Disposed);
        Assert.True(providerFactory.Provider!.StopCalled);
    }

    [Fact]
    public async Task StartAsync_source_start_timeout_cancels_provider_start()
    {
        var providerFactory = new HangingStartMediaSourceProviderFactory();
        var backendFactory = new RecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        engine.StartTimeout = TimeSpan.FromMilliseconds(50);
        engine.StopTimeout = TimeSpan.FromSeconds(1);
        await engine.LoadProjectAsync(CreateValidProject());

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.True(providerFactory.Provider!.StartCancellationObserved);
        Assert.True(providerFactory.Provider.StopCalled);
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
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
            new WinFormsPreviewRenderOutputTarget(123));

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
    public async Task BindOutput_command_timeout_sets_failed_and_requires_cleanup()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var oldSink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        var newSink = new RecordingRenderOutputSink(CreatePreviewTarget(2), "new");
        sinkFactory.Enqueue(oldSink);
        sinkFactory.Enqueue(newSink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));
        backendFactory.Backend!.ResetBindRelease();

        try
        {
            var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

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
    public async Task UnbindOutput_command_timeout_sets_failed_and_requires_cleanup()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        var sink = new RecordingRenderOutputSink(CreatePreviewTarget(1), "old");
        sinkFactory.Enqueue(sink);
        await using var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));
        backendFactory.Backend!.ResetUnbindRelease();

        try
        {
            var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.UnbindOutputAsync(outputId));

            Assert.IsType<TimeoutException>(ex.InnerException);
            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
        }
        finally
        {
            backendFactory.Backend.ReleaseUnbind();
            await engine.DisposeAsync();
        }

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.True(backendFactory.Backend.Disposed);
    }

    [Fact]
    public async Task Command_that_completes_after_timeout_does_not_reenable_engine()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(1), "old"));
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(2), "new"));
        var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));
        backendFactory.Backend!.ResetBindRelease();

        try
        {
            await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));
            Assert.Equal(MediaForgeEngineState.Failed, engine.State);

            backendFactory.Backend.ReleaseBind();
            await WaitUntilAsync(() => backendFactory.Backend.BindCount >= 2, TimeSpan.FromSeconds(5));

            Assert.Equal(MediaForgeEngineState.Failed, engine.State);
            await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
                engine.ApplyProjectUpdateAsync(editor =>
                    editor.Project.Canvases[0].Name = "Still blocked"));
        }
        finally
        {
            backendFactory.Backend.ReleaseBind();
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_after_command_timeout_cleans_render_thread_backend_and_providers()
    {
        var providerFactory = new FakeMediaSourceProviderFactory();
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(1), "old"));
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(2), "new"));
        var engine = CreateEngine(
            providerFactory,
            backendFactory,
            sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));
        backendFactory.Backend!.ResetBindRelease();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));
        backendFactory.Backend.ReleaseBind();

        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
        Assert.True(backendFactory.Backend.Disposed);
        Assert.All(
            providerFactory.Sources.Values,
            source => Assert.Equal(MediaForge.Core.Sources.MediaSourceState.Stopped, source.State));
    }

    [Fact]
    public async Task StopAsync_after_failed_state_attempts_cleanup()
    {
        var providerFactory = new FakeMediaSourceProviderFactory();
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(1), "timed-out"));
        var engine = CreateEngine(
            providerFactory,
            backendFactory,
            sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        backendFactory.Backend!.ResetBindRelease();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(1)));
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);

        backendFactory.Backend.ReleaseBind();
        await WaitUntilAsync(() => backendFactory.Backend.BindCount >= 1, TimeSpan.FromSeconds(5));

        await engine.StopAsync();

        Assert.Equal(MediaForgeEngineState.Loaded, engine.State);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
        Assert.True(backendFactory.Backend.Disposed);
        Assert.All(
            providerFactory.Sources.Values,
            source => Assert.Equal(MediaForge.Core.Sources.MediaSourceState.Stopped, source.State));

        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Failed_state_blocks_operations_but_allows_dispose_cleanup()
    {
        var backendFactory = new CommandTrackingRenderBackendFactory();
        var sinkFactory = new RecordingRenderOutputSinkFactory();
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(1), "old"));
        sinkFactory.Enqueue(new RecordingRenderOutputSink(CreatePreviewTarget(2), "new"));
        var engine = CreateEngine(
            backendFactory: backendFactory,
            outputSinkFactory: sinkFactory);
        engine.CommandTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateValidProject();
        var outputId = project.Outputs[0].Id;
        await engine.LoadProjectAsync(project);
        await engine.StartAsync();
        await engine.BindOutputAsync(outputId, CreatePreviewTarget(1));
        backendFactory.Backend!.ResetBindRelease();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(2)));

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StartAsync());
        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.BindOutputAsync(outputId, CreatePreviewTarget(3)));
        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.AttachSinkAsync(outputId, new FrameNotificationSink()));
        await Assert.ThrowsAsync<MediaForgeEngineException>(() =>
            engine.ApplyProjectUpdateAsync(editor =>
                editor.Project.Canvases[0].Name = "Blocked"));

        backendFactory.Backend.ReleaseBind();
        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.True(backendFactory.Backend.Disposed);
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
        engine.RenderFramesPerSecond = 0.1;
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var source = providerFactory.Sources.Values.First();
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.ActiveSlotRetainCount);

        backendFactory.Backend!.CompleteAllPending();
        await WaitUntilAsync(() => source.ActiveSlotRetainCount == 1, TimeSpan.FromSeconds(5));
        await engine.StopAsync();
        Assert.Equal(0, source.ActiveSlotRetainCount);
    }

    [Fact]
    public async Task Engine_raises_FrameDropped_when_pending_tracker_is_full()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new ManualRecordingRenderBackendFactory();
        await using var engine = CreateEngine(providerFactory, backendFactory);
        var frameDropped = new List<MediaForgeFrameDroppedEventArgs>();
        await engine.LoadProjectAsync(CreateValidProject());
        var canvasId = engine.CurrentProject!.Canvases[0].Id;
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
            Assert.Contains(
                frameDropped,
                args => args.ReasonCode is "render.frame_dropped_tracker_full" or
                    "engine.render_pump_frame_dropped_backpressure" or
                    "engine.frame_scheduler_frame_dropped_backpressure");
        }
        finally
        {
            backendFactory.Backend?.CompleteAllPending();
        }
    }

    [Fact]
    public async Task RenderPump_rate_limits_backpressure_diagnostics()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        await using var pump = new MediaForgeRenderPump(
            framesPerSecond: 120,
            canPublish: () => false,
            publish: () => { },
            diagnostics);

        await Task.Delay(250);
        await pump.StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var backpressureDiagnostics = diagnostics.Diagnostics
            .Count(static diagnostic => diagnostic.Code is
                "engine.render_pump_frame_dropped_backpressure" or
                "engine.frame_scheduler_frame_dropped_backpressure");
        Assert.Equal(1, backpressureDiagnostics);
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
            if (backendFactory.Backend is not null)
                await ReleaseBlockedSubmitAndWaitForRenderThreadAsync(engine, backendFactory.Backend);
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
            if (backendFactory.Backend is not null)
                await ReleaseBlockedSubmitAndWaitForRenderThreadAsync(engine, backendFactory.Backend);
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
            if (backendFactory.Backend is not null)
                await ReleaseBlockedSubmitAndWaitForRenderThreadAsync(engine, backendFactory.Backend);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_after_failed_stop_attempts_cleanup()
    {
        var providerFactory = new GpuFrameSlotRingSourceProviderFactory();
        var backendFactory = new BlockingSubmitRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory);
        engine.RenderThreadJoinTimeoutForTests = TimeSpan.FromMilliseconds(50);

        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        Assert.True(backendFactory.Backend!.WaitForSubmitEntered(TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
        Assert.False(backendFactory.Backend.Disposed);

        await ReleaseBlockedSubmitAndWaitForRenderThreadAsync(engine, backendFactory.Backend);
        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
        Assert.False(engine.RenderThreadForTests?.IsRunning ?? false);
        Assert.True(backendFactory.Backend.Disposed);
        Assert.All(
            providerFactory.Sources.Values,
            source => Assert.Equal(MediaForge.Core.Sources.MediaSourceState.Stopped, source.State));
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
    public async Task Engine_dispose_after_failed_stop_reaches_disposed_after_cleanup()
    {
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        var engine = CreateEngine(providerFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();
        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());

        await engine.DisposeAsync();

        Assert.Equal(MediaForgeEngineState.Disposed, engine.State);
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
        await WaitUntilAsync(() => backendFactory.Backend!.SubmitCount >= 1, TimeSpan.FromSeconds(5));

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
        var engine = CreateEngine(providerFactory, backendFactory);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
        var aggregate = Assert.IsType<AggregateException>(ex.InnerException);

        Assert.True(aggregate.InnerExceptions.Count >= 2);
        Assert.Contains(
            aggregate.InnerExceptions,
            inner => inner.Message.Contains("Simulated provider stop failure", StringComparison.Ordinal));
        Assert.True(backendFactory.Backend!.DisposeAttempted);

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Cleanup_after_failed_state_reports_errors_but_attempts_all_resources()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var providerFactory = new ThrowingStopMediaSourceProviderFactory();
        var backendFactory = new ThrowingDisposeRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory, diagnostics: diagnostics);
        await engine.LoadProjectAsync(CreateValidProject());
        await engine.StartAsync();

        await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.StopAsync());
        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.DisposeAsync().AsTask());
        var aggregate = Assert.IsType<AggregateException>(ex.InnerException);

        Assert.NotEmpty(aggregate.InnerExceptions);
        Assert.True(backendFactory.Backend!.DisposeAttempted);
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "engine.provider_stop_failed");
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "engine.render_backend_dispose_failed");
        Assert.Equal(MediaForgeEngineState.Failed, engine.State);
    }

    [Fact]
    public async Task Engine_dispose_reports_sink_timeout_but_attempts_remaining_cleanup()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var providerFactory = new FakeMediaSourceProviderFactory();
        var backendFactory = new RecordingRenderBackendFactory();
        var engine = CreateEngine(providerFactory, backendFactory, diagnostics: diagnostics);
        engine.RenderFramesPerSecond = 1;
        engine.SinkStopTimeout = TimeSpan.FromMilliseconds(50);
        var project = CreateOffscreenProject();
        var sink = new HungPublicRenderOutputSink();

        await engine.LoadProjectAsync(project);
        await engine.AttachSinkAsync(project.Outputs[0].Id, sink);
        await engine.StartAsync();
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<MediaForgeEngineException>(() => engine.DisposeAsync().AsTask());

        Assert.True(backendFactory.Backend!.Disposed || ex.InnerException is not null);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");

        sink.Release();
        await WaitUntilAsync(() => sink.DisposeCount == 1, TimeSpan.FromSeconds(5));
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

    private static MediaForgeProject GetCurrentProject(MediaForgeEngine engine)
    {
        var project = engine.CurrentProject;
        Assert.NotNull(project);
        return project;
    }

    private static WinFormsPreviewRenderOutputTarget CreatePreviewTarget(nint handle) =>
        new(handle);

    private static MediaForgeProject CreateOffscreenProject() =>
        MediaForgeProjectBuilder.Create()
            .Canvas("Program", 1920, 1080, out var canvas)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                canvas,
                source,
                layer => layer.SetBounds(0, 0, 1920, 1080).SetFit())
            .OffscreenOutput("Program", canvas, 1920, 1080, out _)
            .BuildValidated();

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

    private static MediaForgeProject CreateNestedSceneProject()
    {
        var editor = new MediaForgeProjectEditor(new());
        var source = editor.CreateSource("Desktop", new DesktopCaptureSourceSettings());
        var child = editor.CreateCanvas("Child", new FrameSize(640, 360));
        editor.AddSourceLayer(
            child.Id,
            source.Id,
            new Transform2D { Size = new CanvasSize(640, 360) });

        var parent = editor.CreateCanvas("Parent", new FrameSize(1920, 1080));
        editor.AddCanvasLayer(
            parent.Id,
            child.Id,
            new Transform2D { Size = new CanvasSize(960, 540) });

        editor.CreateOutput(
            "Program",
            parent.Id,
            new PreviewWindowOutputSettings(),
            new FrameSize(1920, 1080));
        editor.ValidateOrThrow();
        return editor.Project;
    }

    private static RenderedOutputFrameBatch CreateRenderedOutputFrameBatch(
        RenderOutputId outputId,
        FrameSize size,
        Func<ValueTask>? leaseReleased = null) =>
        new(
            [
                new RenderedOutputFrame(
                    outputId,
                    size,
                    RenderPixelFormat.Rgba8Unorm,
                    RenderBackendKind.Vulkan)
            ],
            leaseReleased);

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

    private static async Task ReleaseBlockedSubmitAndWaitForRenderThreadAsync(
        MediaForgeEngine engine,
        BlockingSubmitRenderBackend backend)
    {
        backend.ReleaseSubmit();
        Assert.True(backend.WaitForSubmitExited(TimeSpan.FromSeconds(5)));
        await WaitUntilAsync(
            () => !(engine.RenderThreadForTests?.IsRunning ?? false),
            TimeSpan.FromSeconds(5));
    }
}
