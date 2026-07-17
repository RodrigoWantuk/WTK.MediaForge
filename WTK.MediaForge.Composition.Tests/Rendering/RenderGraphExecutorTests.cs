using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
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
        Assert.Equal(1, result.PhysicalPlan.Statistics.CanvasPasses);
        Assert.Equal(2, result.PhysicalPlan.Statistics.OutputPasses);
        Assert.Equal(1, result.PhysicalPlan.Statistics.FanOutGroups);
        Assert.Equal(1, result.PhysicalPlan.Statistics.ReusedCanvasOutputs);
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
    public void Physical_plan_exposes_source_reuse_placement_dependent_effect_canvas_and_output_passes()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Preview", 1280, 720, out var preview)
            .Scene("Program", 1920, 1080, out var program)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                preview,
                source,
                layer => layer
                    .SetBounds(0, 0, 1280, 720)
                    .AddBlur(4))
            .AddSourceLayer(
                program,
                source,
                layer => layer
                    .SetBounds(100, 100, 640, 360)
                    .AddBlur(4))
            .OffscreenOutput("Preview full", preview, 1280, 720, out _)
            .OffscreenOutput("Preview half", preview, 640, 360, out _)
            .OffscreenOutput("Program", program, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var physical = plan.PhysicalPlan;

        Assert.Equal(1, physical.Count(PhysicalRenderGraphOperationKind.AcquireSourceFrame));
        Assert.Equal(2, physical.Count(PhysicalRenderGraphOperationKind.RenderEffectIntermediate));
        Assert.Equal(2, physical.Count(PhysicalRenderGraphOperationKind.RenderCanvas));
        Assert.Equal(3, physical.Count(PhysicalRenderGraphOperationKind.RenderOutput));
        Assert.Equal(1, physical.Count(PhysicalRenderGraphOperationKind.FanOutRenderedOutput));
        Assert.True(physical.Statistics.ReusedSourceConsumers >= 1);
        Assert.Equal(1, physical.Statistics.ReusedCanvasOutputs);
    }

    [Fact]
    public void Transition_snapshot_physical_plan_has_explicit_transition_operation()
    {
        var sourceId = SourceId.New();
        var outputId = RenderOutputId.New();
        var previousCanvasId = CanvasId.New();
        var currentCanvasId = CanvasId.New();
        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 10,
            Canvases =
            [
                CreateSourceCanvas(previousCanvasId, sourceId),
                CreateSourceCanvas(currentCanvasId, sourceId)
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program",
                    CanvasId = currentCanvasId,
                    PreviousCanvasId = previousCanvasId,
                    RouteTransitionKind = OutputRouteTransitionKind.Fade,
                    RouteTransitionProgress = 0.5f,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };

        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot);
        var transitionNode = Assert.Single(
            plan.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.OutputTransition);
        var outputNode = Assert.Single(
            plan.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.OutputPass);

        Assert.Equal(1, plan.Count(MediaForgeRenderGraphNodeKind.SourceFrame));
        Assert.Equal(2, plan.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Contains(transitionNode.Key, outputNode.Dependencies);
        Assert.Equal(2, transitionNode.Dependencies.Count);
        Assert.Equal(outputId, transitionNode.OutputId);
        Assert.Equal(currentCanvasId, transitionNode.CanvasId);
        Assert.Equal(previousCanvasId, transitionNode.PreviousCanvasId);
        Assert.Equal(outputId, outputNode.OutputId);
        Assert.Equal(currentCanvasId, outputNode.CanvasId);
        Assert.Equal(1, plan.PhysicalPlan.Count(PhysicalRenderGraphOperationKind.RenderOutputTransition));
        Assert.Equal(1, plan.PhysicalPlan.Statistics.OutputTransitionPasses);
        var physicalTransition = Assert.Single(
            plan.PhysicalPlan.Operations,
            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutputTransition);
        var physicalOutput = Assert.Single(
            plan.PhysicalPlan.Operations,
            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutput);
        Assert.Equal(outputId, physicalTransition.OutputId);
        Assert.Equal(currentCanvasId, physicalTransition.CanvasId);
        Assert.Equal(previousCanvasId, physicalTransition.PreviousCanvasId);
        Assert.Equal(outputId, physicalOutput.OutputId);
        Assert.Equal(currentCanvasId, physicalOutput.CanvasId);

        var result = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = 8,
                    FrameBudget = TimeSpan.FromSeconds(1d / 60d)
                },
                SourceFrames = CreateSourceFrames(sourceId)
            });

        Assert.Contains(transitionNode.Key, result.ExecutedNodeKeys);
        Assert.Contains(outputNode.Key, result.ExecutedNodeKeys);
        Assert.True(result.NodeResults[transitionNode.Key].HasRenderableResource);
        Assert.True(result.NodeResults[outputNode.Key].HasRenderableResource);
    }

    [Fact]
    public void Render_snapshot_graph_compilation_tolerates_non_finite_effect_values_for_later_diagnostics()
    {
        var sourceId = SourceId.New();
        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 12,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Invalid effect scene",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Source",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) },
                            Effects =
                            [
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Invalid key",
                                    Similarity = float.PositiveInfinity
                                }
                            ]
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program",
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };

        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot);

        Assert.Equal(1, plan.Count(MediaForgeRenderGraphNodeKind.SourceEffectChain));
        Assert.Equal(1, plan.PhysicalPlan.Count(PhysicalRenderGraphOperationKind.RenderEffectIntermediate));
    }

    [Fact]
    public void Blur_effect_intermediate_carries_canvas_source_and_draw_object_metadata()
    {
        var sourceId = SourceId.New();
        var drawObjectId = DrawObjectId.New();
        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 13,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Blur scene",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = drawObjectId,
                            Name = "Blurred source",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) },
                            Effects =
                            [
                                new BlurEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Blur",
                                    Radius = 6f
                                }
                            ]
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program",
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };

        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot);
        var effectNode = Assert.Single(
            plan.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.SourceEffectChain);
        var effectOperation = Assert.Single(
            plan.PhysicalPlan.Operations,
            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate);

        Assert.Equal(canvasId, effectNode.CanvasId);
        Assert.Equal(sourceId, effectNode.SourceId);
        Assert.Equal(drawObjectId, effectNode.DrawObjectId);
        Assert.Equal(canvasId, effectOperation.CanvasId);
        Assert.Equal(sourceId, effectOperation.SourceId);
        Assert.Equal(drawObjectId, effectOperation.DrawObjectId);
    }

    [Fact]
    public void Primitive_layer_canvas_executes_without_source_frame()
    {
        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 11,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Slate",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Background",
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program",
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };

        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot);
        var result = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = 9,
                    FrameBudget = TimeSpan.FromSeconds(1d / 60d)
                }
            });

        Assert.Equal(1, plan.Count(MediaForgeRenderGraphNodeKind.PrimitiveLayer));
        Assert.Equal(1, plan.PhysicalPlan.Count(PhysicalRenderGraphOperationKind.RenderPrimitiveLayer));
        Assert.Contains(result.ExecutedNodeKeys, key => key.StartsWith("primitive:", StringComparison.Ordinal));
        Assert.Contains(result.ExecutedNodeKeys, key => key.StartsWith("canvas:", StringComparison.Ordinal));
        Assert.Contains(result.ExecutedNodeKeys, key => key.StartsWith("output:", StringComparison.Ordinal));
    }

    [Fact]
    public void Execution_result_contains_stable_node_results_snapshot()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var plan = MediaForgeRenderGraphCompiler.Compile(project);
        var context = new RenderGraphContext
        {
            FrameContext = new FrameExecutionContext
            {
                FrameId = 7,
                FrameBudget = TimeSpan.FromSeconds(1d / 60d)
            },
            SourceFrames = CreateSourceFrames(source.Id)
        };

        var result = RenderGraphExecutor.Execute(plan, context);

        Assert.NotEmpty(result.NodeResults);
        Assert.Equal(context.NodeResults.Count, result.NodeResults.Count);

        context.NodeResults.Clear();

        Assert.NotEmpty(result.NodeResults);
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

    private static RenderCanvasSnapshot CreateSourceCanvas(CanvasId canvasId, SourceId sourceId) =>
        new()
        {
            Id = canvasId,
            Name = "Scene",
            Size = new FrameSize(1920, 1080),
            Objects =
            [
                new RenderSourceLayerDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = "Source",
                    SourceId = sourceId,
                    Transform = new Transform2D { Size = new CanvasSize(1920, 1080) }
                }
            ]
        };
}
