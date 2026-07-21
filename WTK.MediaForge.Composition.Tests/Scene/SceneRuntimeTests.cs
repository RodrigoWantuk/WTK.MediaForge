using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scene;

public sealed class SceneRuntimeTests
{
    [Fact]
    public void Explicit_output_binding_materializes_the_requested_canvas_version()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(CreateVersionedProjectState(canvasId, outputId, "Published v1", SceneVersionBinding.Published));
        var firstVersion = runtime.GetPublishedVersion(canvasId);

        runtime.SyncFrom(CreateVersionedProjectState(
            canvasId,
            outputId,
            "Published v2",
            SceneVersionBinding.ExplicitVersion(firstVersion)));

        using var compositionRuntime = new CompositionRuntime();
        using var result = runtime.BuildRenderSnapshot(
            compositionRuntime,
            RenderFrameSnapshotFactory.CreateDefaultContext());
        var snapshot = Assert.IsType<RenderFrameSnapshot>(result.TakeSnapshot());
        try
        {
            var output = Assert.Single(snapshot.Outputs);
            Assert.Equal(canvasId, output.CanvasId);
            var resolvedCanvas = Assert.Single(
                snapshot.Canvases.Where(canvas => canvas.PhysicalKey == output.ResolvedCanvasKey));
            Assert.Equal("Published v1", resolvedCanvas.Name);
            Assert.Equal(firstVersion, resolvedCanvas.VersionId);
            var publishedCanvas = Assert.Single(
                snapshot.Canvases.Where(canvas => canvas.Id == canvasId && canvas.Name == "Published v2"));
            Assert.NotEqual(publishedCanvas.PhysicalKey, resolvedCanvas.PhysicalKey);
        }
        finally
        {
            snapshot.Dispose();
        }
    }

    [Fact]
    public void Draft_output_binding_materializes_draft_without_replacing_published_canvas()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();
        var sessionId = SceneEditSessionId.New();
        var publishedState = CreateVersionedProjectState(
            canvasId,
            outputId,
            "Published",
            SceneVersionBinding.DraftForSession(sessionId));
        runtime.SyncFrom(publishedState);
        var publishedVersion = runtime.GetPublishedVersion(canvasId);
        var draftVersion = SceneVersionId.New();
        var draftState = CreateVersionedProjectState(
            canvasId,
            outputId,
            "Draft",
            SceneVersionBinding.DraftForSession(sessionId));
        runtime.UpsertDraft(
            new SceneDraftState
            {
                SessionId = sessionId,
                CanvasId = canvasId,
                BasePublishedVersionId = publishedVersion,
                DraftVersionId = draftVersion,
                HasChanges = true
            },
            draftState);

        using var compositionRuntime = new CompositionRuntime();
        using var result = runtime.BuildRenderSnapshot(
            compositionRuntime,
            RenderFrameSnapshotFactory.CreateDefaultContext());
        var snapshot = Assert.IsType<RenderFrameSnapshot>(result.TakeSnapshot());
        ResolvedCanvasKey firstResolvedKey;
        try
        {
            var output = Assert.Single(snapshot.Outputs);
            Assert.Equal(canvasId, output.CanvasId);
            var resolvedCanvas = Assert.Single(
                snapshot.Canvases.Where(canvas => canvas.PhysicalKey == output.ResolvedCanvasKey));
            Assert.Equal("Draft", resolvedCanvas.Name);
            Assert.Equal(draftVersion, resolvedCanvas.VersionId);
            Assert.Contains(snapshot.Canvases, canvas => canvas.Id == canvasId && canvas.Name == "Published");
            Assert.NotEqual(
                snapshot.Canvases.Single(canvas => canvas.Name == "Published").PhysicalKey,
                resolvedCanvas.PhysicalKey);
            firstResolvedKey = resolvedCanvas.PhysicalKey;
        }
        finally
        {
            snapshot.Dispose();
        }

        using var secondResult = runtime.BuildRenderSnapshot(
            compositionRuntime,
            RenderFrameSnapshotFactory.CreateDefaultContext());
        using var secondSnapshot = Assert.IsType<RenderFrameSnapshot>(secondResult.TakeSnapshot());
        Assert.Equal(firstResolvedKey, Assert.Single(secondSnapshot.Outputs).ResolvedCanvasKey);
    }

    [Fact]
    public void Equivalent_explicit_bindings_share_one_resolved_canvas_identity()
    {
        var canvasId = CanvasId.New();
        var firstOutputId = RenderOutputId.New();
        var secondOutputId = RenderOutputId.New();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(CreateVersionedProjectState(
            canvasId,
            firstOutputId,
            "Published v1",
            SceneVersionBinding.Published));
        var sharedVersion = runtime.GetPublishedVersion(canvasId);

        var updated = CreateVersionedProjectState(
            canvasId,
            firstOutputId,
            "Published v2",
            SceneVersionBinding.ExplicitVersion(sharedVersion));
        var firstOutput = updated.Outputs[0];
        updated = updated with
        {
            Outputs =
            [
                firstOutput,
                new RenderOutputStateSnapshot
                {
                    Id = secondOutputId,
                    Name = "Second output",
                    CanvasId = canvasId,
                    OutputSize = firstOutput.OutputSize,
                    SceneVersionBinding = SceneVersionBinding.ExplicitVersion(sharedVersion),
                    RouteTransitionKind = OutputRouteTransitionKind.Cut
                }
            ]
        };
        runtime.SyncFrom(updated);

        using var compositionRuntime = new CompositionRuntime();
        using var result = runtime.BuildRenderSnapshot(
            compositionRuntime,
            RenderFrameSnapshotFactory.CreateDefaultContext());
        using var snapshot = Assert.IsType<RenderFrameSnapshot>(result.TakeSnapshot());

        Assert.Equal(2, snapshot.Outputs.Length);
        Assert.Single(snapshot.Outputs.Select(static output => output.ResolvedCanvasKey).Distinct());
        Assert.Single(snapshot.Canvases.Where(canvas => canvas.VersionId == sharedVersion));
    }

    [Fact]
    public void Project_resynchronization_releases_stale_draft_version_pin()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var sessionId = SceneEditSessionId.New();
        var runtime = new SceneRuntime();
        var published = CreateVersionedProjectState(
            canvasId,
            outputId,
            "Published",
            SceneVersionBinding.Published);
        runtime.SyncFrom(published);
        var publishedVersion = runtime.GetPublishedVersion(canvasId);
        var draftVersion = SceneVersionId.New();

        runtime.UpsertDraft(
            new SceneDraftState
            {
                SessionId = sessionId,
                CanvasId = canvasId,
                BasePublishedVersionId = publishedVersion,
                DraftVersionId = draftVersion,
                HasChanges = true
            },
            CreateVersionedProjectState(
                canvasId,
                outputId,
                "Draft",
                SceneVersionBinding.DraftForSession(sessionId)));

        Assert.Equal(2, runtime.VersionRetentionSnapshot.PinnedVersionCount);

        runtime.SyncFrom(published);

        Assert.Equal(1, runtime.VersionRetentionSnapshot.PinnedVersionCount);
        Assert.False(runtime.TryGetDraft(sessionId, out _));
    }

    [Fact]
    public void Scene_version_history_is_bounded_but_keeps_explicitly_bound_versions()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(CreateVersionedProjectState(canvasId, outputId, "v0", SceneVersionBinding.Published));
        var pinnedVersion = runtime.GetPublishedVersion(canvasId);

        for (var revision = 1; revision <= SceneVersionStore.MaximumRetainedVersionsPerCanvas + 8; revision++)
        {
            runtime.SyncFrom(CreateVersionedProjectState(
                canvasId,
                outputId,
                $"v{revision}",
                SceneVersionBinding.ExplicitVersion(pinnedVersion)));
        }

        var snapshot = runtime.CreateSnapshot().ProjectState;
        Assert.True(snapshot.CanvasVersionSnapshots.Count <= SceneVersionStore.MaximumRetainedVersionsPerCanvas + 1);
        Assert.Contains(pinnedVersion, snapshot.CanvasVersionSnapshots.Keys);
        Assert.Contains(runtime.GetPublishedVersion(canvasId), snapshot.CanvasVersionSnapshots.Keys);
    }

    [Fact]
    public void Ten_thousand_scene_versions_remain_bounded_and_report_retention_health()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();

        for (var revision = 0; revision < 10_000; revision++)
        {
            runtime.SyncFrom(CreateVersionedProjectState(
                canvasId,
                outputId,
                $"revision-{revision}",
                SceneVersionBinding.Published));
        }

        var retention = runtime.VersionRetentionSnapshot;
        Assert.Equal(SceneVersionStore.MaximumRetainedVersionsPerCanvas, retention.RetainedVersionCount);
        Assert.Equal(1, retention.PinnedVersionCount);
        Assert.True(retention.DiscardedVersionCount >= 10_000 - SceneVersionStore.MaximumRetainedVersionsPerCanvas);
        Assert.True(retention.HighWaterMark <= SceneVersionStore.MaximumRetainedVersionsPerCanvas + 1);
    }

    [Fact]
    public void Transition_version_graph_pins_survive_history_trimming_and_release_to_baseline()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(CreateVersionedProjectState(canvasId, outputId, "previous", SceneVersionBinding.Published));
        var previousVersion = runtime.GetPublishedVersion(canvasId);
        runtime.SyncFrom(CreateVersionedProjectState(canvasId, outputId, "current", SceneVersionBinding.Published));
        var currentVersion = runtime.GetPublishedVersion(canvasId);
        var previousGraph = new SceneVersionGraph(
            canvasId,
            new Dictionary<CanvasId, SceneVersionId> { [canvasId] = previousVersion });
        var currentGraph = new SceneVersionGraph(
            canvasId,
            new Dictionary<CanvasId, SceneVersionId> { [canvasId] = currentVersion });

        using (runtime.PinVersionGraphs(previousGraph, currentGraph, "test-transition"))
        {
            for (var revision = 0; revision < 100; revision++)
            {
                runtime.SyncFrom(CreateVersionedProjectState(
                    canvasId,
                    outputId,
                    $"after-{revision}",
                    SceneVersionBinding.Published));
            }

            var pinnedState = runtime.CreateSnapshot().ProjectState;
            Assert.Contains(previousVersion, pinnedState.CanvasVersionSnapshots.Keys);
            Assert.Contains(currentVersion, pinnedState.CanvasVersionSnapshots.Keys);
            Assert.Equal(3, runtime.VersionRetentionSnapshot.PinnedVersionCount);
        }

        runtime.SyncFrom(CreateVersionedProjectState(canvasId, outputId, "trim-after-release", SceneVersionBinding.Published));

        var releasedState = runtime.CreateSnapshot().ProjectState;
        Assert.DoesNotContain(previousVersion, releasedState.CanvasVersionSnapshots.Keys);
        Assert.DoesNotContain(currentVersion, releasedState.CanvasVersionSnapshots.Keys);
        Assert.Equal(1, runtime.VersionRetentionSnapshot.PinnedVersionCount);
        Assert.Equal(SceneVersionStore.MaximumRetainedVersionsPerCanvas, runtime.VersionRetentionSnapshot.RetainedVersionCount);
    }

    [Fact]
    public void Hidden_layer_skips_render_node()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        var runtime = new SceneRuntime();
        runtime.SyncFrom(projectState);

        var layerId = projectState.Canvases[0].Objects[0].Id;
        runtime.SetLayerVisible(layerId, isVisible: false);

        var snapshot = runtime.CreateSnapshot();

        Assert.Equal(
            0,
            snapshot.CachedRenderGraphPlan!.Count(
                global::WTK.MediaForge.Composition.Runtime.Rendering.MediaForgeRenderGraphNodeKind.SourceFrame));
    }

    [Fact]
    public void Dirty_transform_only_marks_transform_subgraph()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var runtime = new SceneRuntime();
        var initialState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(initialState);

        var canvas = project.Canvases[0];
        var layer = canvas.Objects[0];
        layer.Transform = layer.Transform with
        {
            Position = new CanvasPoint(layer.Transform.Position.X + 10f, layer.Transform.Position.Y)
        };

        var updatedState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(updatedState);

        var snapshot = runtime.CreateSnapshot();
        var dirtyKind = Assert.Single(snapshot.DirtyRegion.LayerDirtyKinds.Values);

        Assert.Equal(SceneDirtyKind.Transform, dirtyKind);
        Assert.False(snapshot.DirtyRegion.RequiresGraphRecompile);
    }

    [Fact]
    public void Blur_radius_change_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [new BlurEffect { Radius = 4f, Order = 0 }],
            effects => ((BlurEffect)effects[0]).Radius = 12f);
    }

    [Fact]
    public void Chroma_similarity_change_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [
                new ChromaKeyEffect
                {
                    KeyColor = ColorRgba.From(0f, 1f, 0f, 1f),
                    Similarity = 0.4f,
                    Smoothness = 0.08f,
                    SpillReduction = 0.5f,
                    Order = 0
                }
            ],
            effects => ((ChromaKeyEffect)effects[0]).Similarity = 0.7f);
    }

    [Fact]
    public void Color_brightness_change_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [new ColorCorrectionEffect { Brightness = 0f, Contrast = 1f, Saturation = 1f, Order = 0 }],
            effects => ((ColorCorrectionEffect)effects[0]).Brightness = 0.2f);
    }

    [Fact]
    public void Effect_reorder_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [
                new ColorCorrectionEffect { Brightness = 0.1f, Order = 0 },
                new ChromaKeyEffect { Similarity = 0.4f, Order = 1 }
            ],
            effects =>
            {
                effects[0].Order = 1;
                effects[1].Order = 0;
            });
    }

    [Fact]
    public void Effect_enable_change_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [new BlurEffect { Radius = 4f, Enabled = true, Order = 0 }],
            effects => effects[0].Enabled = false);
    }

    [Fact]
    public void Effect_type_change_updates_scene_version_and_marks_effects_dirty()
    {
        AssertEffectMutationChangesVersionAndDirty(
            [new BlurEffect { Radius = 4f, Order = 0 }],
            effects =>
            {
                var effectId = effects[0].Id;
                effects[0] = new ColorCorrectionEffect
                {
                    Id = effectId,
                    Brightness = 0.1f,
                    Contrast = 1f,
                    Saturation = 1f,
                    Order = 0
                };
            });
    }

    [Fact]
    public void Scene_runtime_preserves_resource_refs_across_frames()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var runtime = new SceneRuntime();
        var projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(projectState);
        runtime.SyncFrom(projectState);

        var resourceRef = Assert.Single(runtime.ResourceRefCounts);
        Assert.Equal(source.Id, resourceRef.Key);
        Assert.Equal(1, resourceRef.Value);
    }

    [Fact]
    public void Hidden_layer_remains_hidden_after_sync_from_updated_project_state()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var runtime = new SceneRuntime();
        var initialState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(initialState);

        var layer = project.Canvases[0].Objects[0];
        runtime.SetLayerVisible(layer.Id, isVisible: false);

        layer.Transform = layer.Transform with
        {
            Position = new CanvasPoint(100, 100)
        };

        var updatedState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(updatedState);

        var snapshot = runtime.CreateSnapshot();

        Assert.Contains(layer.Id, snapshot.HiddenLayerIds);
        Assert.False(snapshot.Layers[layer.Id].IsVisible);
        Assert.Empty(snapshot.ProjectState.Canvases[0].Objects);
    }

    [Fact]
    public void Removed_layer_is_pruned_from_hidden_layer_set()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var runtime = new SceneRuntime();
        var initialState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(initialState);

        var removedLayerId = project.Canvases[0].Objects[0].Id;
        runtime.SetLayerVisible(removedLayerId, isVisible: false);
        project.Canvases[0].Objects.Clear();

        var updatedState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(updatedState);

        var snapshot = runtime.CreateSnapshot();

        Assert.DoesNotContain(removedLayerId, snapshot.HiddenLayerIds);
        Assert.False(snapshot.Layers.ContainsKey(removedLayerId));
    }

    private static void AssertEffectMutationChangesVersionAndDirty(
        IReadOnlyList<MediaForgeEffect> initialEffects,
        Action<List<MediaForgeEffect>> mutate)
    {
        var project = CreateSourceLayerProject();
        var layer = project.Canvases[0].Objects[0];
        layer.Effects.AddRange(initialEffects);

        var runtime = new SceneRuntime();
        var initialState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(initialState);
        var canvasId = initialState.Canvases[0].Id;
        var oldVersionId = runtime.PublishedStates[canvasId].VersionId;

        mutate(layer.Effects);

        var updatedState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        runtime.SyncFrom(updatedState);
        var snapshot = runtime.CreateSnapshot();
        var dirtyKind = Assert.Single(snapshot.DirtyRegion.LayerDirtyKinds.Values);

        Assert.NotEqual(oldVersionId, runtime.PublishedStates[canvasId].VersionId);
        Assert.Equal(SceneDirtyKind.Effects, dirtyKind);
        Assert.True(snapshot.DirtyRegion.RequiresGraphRecompile);
    }

    private static MediaForgeProject CreateSourceLayerProject() =>
        MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

    private static ProjectStateSnapshot CreateVersionedProjectState(
        CanvasId canvasId,
        RenderOutputId outputId,
        string canvasName,
        SceneVersionBinding binding) =>
        new()
        {
            Version = Random.Shared.NextInt64(),
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = canvasId,
                    Name = canvasName,
                    Size = new FrameSize(1920, 1080),
                    Objects = []
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Output",
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080),
                    SceneVersionBinding = binding,
                    RouteTransitionKind = OutputRouteTransitionKind.Cut
                }
            ]
        };
}
