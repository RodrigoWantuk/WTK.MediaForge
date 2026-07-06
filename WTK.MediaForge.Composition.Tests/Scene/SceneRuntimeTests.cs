using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scene;

public sealed class SceneRuntimeTests
{
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

        Assert.Equal(0, snapshot.CachedRenderGraphPlan!.Count(Runtime.Rendering.MediaForgeRenderGraphNodeKind.SourceFrame));
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

        Assert.Equal(1, runtime.ResourceRefCounts.Count);
        Assert.Equal(1, runtime.ResourceRefCounts[source.Id]);
    }
}
