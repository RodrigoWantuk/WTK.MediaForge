using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
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
                SceneSnapshot = sceneRuntime.CreateSnapshot(),
                SourceFrames = CreateSourceFrames(source.Id)
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
                },
                SourceFrames = CreateSourceFrames(source.Id)
            });

        Assert.Equal(1, result.ExecutedNodeKeys.Count(key => key.StartsWith("canvas:", StringComparison.Ordinal)));
        Assert.Equal(2, result.ExecutedNodeKeys.Count(key => key.StartsWith("output:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Source_node_without_frame_skips_downstream_work()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var result = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = 3,
                    FrameBudget = TimeSpan.FromSeconds(1d / 60d)
                }
            });

        Assert.Empty(result.ExecutedNodeKeys);
        Assert.Contains(result.SkippedNodeKeys, key => key.StartsWith("source:", StringComparison.Ordinal));
        Assert.Contains(result.SkippedNodeKeys, key => key.StartsWith("canvas:", StringComparison.Ordinal));
        Assert.Contains(result.SkippedNodeKeys, key => key.StartsWith("output:", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_frame_resource_propagates_to_output_node()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var sourceFrame = CreateFrame(source.Id);
        var context = new RenderGraphContext
        {
            FrameContext = new FrameExecutionContext
            {
                FrameId = 4,
                FrameBudget = TimeSpan.FromSeconds(1d / 60d)
            },
            SourceFrames = new Dictionary<SourceId, GpuFrameReference>
            {
                [source.Id] = sourceFrame
            }
        };

        var result = RenderGraphExecutor.Execute(plan, context);
        var outputKey = Assert.Single(result.ExecutedNodeKeys, key => key.StartsWith("output:", StringComparison.Ordinal));

        if (context.NodeResults[outputKey].SourceFrame is not { } outputFrame)
            throw new Xunit.Sdk.XunitException("Output node did not receive a source frame.");
        Assert.Equal(sourceFrame, outputFrame);
    }

    [Fact]
    public void Canvas_uses_available_source_frame_when_another_source_is_missing()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop A", displayIndex: 0, out var availableSource)
            .DesktopSource("Desktop B", displayIndex: 1, out var missingSource)
            .AddSourceLayer(scene, missingSource, layer => layer.SetBounds(0, 0, 960, 1080))
            .AddSourceLayer(scene, availableSource, layer => layer.SetBounds(960, 0, 960, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var sourceFrame = CreateFrame(availableSource.Id);
        var context = new RenderGraphContext
        {
            FrameContext = new FrameExecutionContext
            {
                FrameId = 5,
                FrameBudget = TimeSpan.FromSeconds(1d / 60d)
            },
            SourceFrames = new Dictionary<SourceId, GpuFrameReference>
            {
                [availableSource.Id] = sourceFrame
            }
        };

        var result = RenderGraphExecutor.Execute(plan, context);

        Assert.Contains(result.SkippedNodeKeys, key => key == $"source:{missingSource.Id}");
        Assert.Contains(result.ExecutedNodeKeys, key => key.StartsWith("canvas:", StringComparison.Ordinal));
        var outputKey = Assert.Single(result.ExecutedNodeKeys, key => key.StartsWith("output:", StringComparison.Ordinal));
        if (context.NodeResults[outputKey].SourceFrame is not { } outputFrame)
            throw new Xunit.Sdk.XunitException("Output node did not receive a source frame.");
        Assert.Equal(sourceFrame, outputFrame);
    }

    [Fact]
    public void Output_node_skips_when_dependency_produces_no_resource()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Empty", 1920, 1080, out var scene)
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var context = new RenderGraphContext
        {
            FrameContext = new FrameExecutionContext
            {
                FrameId = 6,
                FrameBudget = TimeSpan.FromSeconds(1d / 60d)
            }
        };

        var result = RenderGraphExecutor.Execute(plan, context);
        var outputKey = Assert.Single(result.SkippedNodeKeys, key => key.StartsWith("output:", StringComparison.Ordinal));

        Assert.True(context.NodeResults[outputKey].WasSkipped);
        Assert.Contains("renderable", context.NodeResults[outputKey].FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<SourceId, GpuFrameReference> CreateSourceFrames(SourceId sourceId) =>
        new Dictionary<SourceId, GpuFrameReference>
        {
            [sourceId] = CreateFrame(sourceId)
        };

    private static GpuFrameReference CreateFrame(SourceId sourceId) =>
        new()
        {
            SourceId = sourceId,
            Backend = GpuFrameBackend.D3D11SharedTexture,
            TextureSize = new FrameSize(1920, 1080),
            LogicalSize = new FrameSize(1920, 1080),
            FrameNumber = 1,
            Timestamp = MediaTime.Zero
        };
}
