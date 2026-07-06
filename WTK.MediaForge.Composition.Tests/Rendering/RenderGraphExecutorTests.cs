using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Rendering;

public sealed class RenderGraphExecutorTests
{
    [Fact]
    public void RenderGraphExecutor_respects_node_dependencies()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                scene,
                source,
                layer => layer
                    .SetBounds(0, 0, 1920, 1080)
                    .AddChromaKey(Core.Color.ColorRgba.From(0, 1, 0, 1)))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        var plan = MediaForgeRenderGraphCompiler.Compile(projectState);
        var sceneRuntime = new SceneRuntime();
        sceneRuntime.SyncFrom(projectState);

        var result = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = 1,
                    FrameBudget = TimeSpan.FromSeconds(1d / 60d)
                },
                SceneSnapshot = sceneRuntime.CreateSnapshot()
            });

        var graph = RenderGraphBuilder.FromPlan(plan);
        var sourceIndex = graph.TopologicallySorted
            .ToList()
            .FindIndex(node => node.Kind == RenderGraphNodeKind.Source);
        var transformIndex = graph.TopologicallySorted
            .ToList()
            .FindIndex(node => node.Kind == RenderGraphNodeKind.Transform);
        var blendIndex = graph.TopologicallySorted
            .ToList()
            .FindIndex(node => node.Kind == RenderGraphNodeKind.Blend);
        var outputIndex = graph.TopologicallySorted
            .ToList()
            .FindIndex(node => node.Kind == RenderGraphNodeKind.Output);

        Assert.True(sourceIndex < transformIndex);
        Assert.True(transformIndex < blendIndex);
        Assert.True(blendIndex < outputIndex);
        Assert.NotEmpty(result.ExecutedNodeKeys);
    }

    [Fact]
    public void Identical_subgraphs_execute_once_per_frame()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Full", scene, 1920, 1080, out _)
            .OffscreenOutput("Half", scene, 960, 540, out _)
            .BuildValidated();

        var projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        var plan = MediaForgeRenderGraphCompiler.Compile(projectState);

        var result = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = 2,
                    FrameBudget = TimeSpan.FromSeconds(1d / 60d)
                }
            });

        Assert.Equal(1, result.ExecutedNodeKeys.Count(key => key.StartsWith("canvas:", StringComparison.Ordinal)));
        Assert.Equal(2, result.ExecutedNodeKeys.Count(key => key.StartsWith("output:", StringComparison.Ordinal)));
    }
}
