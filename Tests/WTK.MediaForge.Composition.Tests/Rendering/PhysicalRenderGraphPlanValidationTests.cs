using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Rendering;

public sealed class PhysicalRenderGraphPlanValidationTests
{
    [Fact]
    public void Compiled_plan_is_valid_for_its_render_snapshot()
    {
        using var snapshot = CreateSnapshot();
        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot).PhysicalPlan;

        plan.ValidateFor(snapshot);
    }

    [Fact]
    public void Compiled_effect_plan_covers_source_and_layer_effect_stacks()
    {
        using var snapshot = CreateSourceSnapshot(
            out _,
            includeSourceEffects: true,
            includeLayerEffects: true);
        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot).PhysicalPlan;

        plan.ValidateFor(snapshot);

        Assert.Equal(2, plan.Count(PhysicalRenderGraphOperationKind.RenderEffectIntermediate));
    }

    [Fact]
    public void Plan_rejects_missing_dependency_before_gpu_submission()
    {
        using var snapshot = CreateSnapshot();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:invalid",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                Dependencies = ["canvas:missing"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("depends on missing operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_snapshot_output_without_exactly_one_physical_output_pass()
    {
        using var snapshot = CreateSnapshot();
        var canvas = snapshot.Canvases.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:only",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("do not match the render snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_operation_before_its_dependency()
    {
        using var snapshot = CreateSnapshot();
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:first",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                Dependencies = ["canvas:late"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:late",
                CanvasId = canvas.Id,
                Consumers = ["output:first"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("not topologically ordered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_dependency_without_a_reciprocal_consumer_link()
    {
        using var snapshot = CreateSnapshot();
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:program",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = canvas.Id,
                Dependencies = ["canvas:program"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("does not declare 'output:program' as a consumer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_primitive_pass_without_a_snapshot_draw_object()
    {
        using var snapshot = CreateSnapshot();
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderPrimitiveLayer,
                Key = "primitive:invalid",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                DrawObjectId = DrawObjectId.New(),
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = canvas.Id,
                Dependencies = ["primitive:invalid"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));
        Assert.Contains("must identify its canvas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_output_pass_with_a_resolved_canvas_other_than_its_output_binding()
    {
        using var snapshot = CreateSnapshot();
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:program",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                ResolvedCanvasKey = ResolvedCanvasKey.Unversioned(CanvasId.New()),
                Dependencies = ["canvas:program"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("must identify the canvas and resolved canvas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_transition_pass_with_a_previous_canvas_other_than_its_output_binding()
    {
        using var snapshot = CreateTransitionSnapshot();
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:program",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Consumers = ["transition:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutputTransition,
                Key = "transition:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                ResolvedCanvasKey = canvas.PhysicalKey,
                PreviousCanvasId = CanvasId.New(),
                PreviousResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["canvas:program"],
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["transition:program"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("must identify the current and previous canvases", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_source_acquisition_for_a_source_absent_from_the_snapshot()
    {
        using var snapshot = CreateSnapshot();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                Key = "source:missing",
                SourceId = SourceId.New(),
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                Dependencies = ["source:missing"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));
        Assert.Contains("source absent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_enabled_source_layer_without_an_explicit_physical_layer_pass()
    {
        using var snapshot = CreateSourceSnapshot(out var sourceId);
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                Key = "source:camera",
                SourceId = sourceId,
                Consumers = ["canvas:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:program",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["source:camera"],
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["canvas:program"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("enabled source layer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_enabled_nested_canvas_without_an_explicit_physical_layer_pass()
    {
        using var snapshot = CreateNestedCanvasSnapshot(out var parent, out var child, out var output);
        var plan = new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:child",
                CanvasId = child.Id,
                ResolvedCanvasKey = child.PhysicalKey,
                Consumers = ["canvas:parent"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:parent",
                CanvasId = parent.Id,
                ResolvedCanvasKey = parent.PhysicalKey,
                Dependencies = ["canvas:child"],
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = parent.Id,
                ResolvedCanvasKey = parent.PhysicalKey,
                Dependencies = ["canvas:parent"]
            }
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("enabled nested canvas layer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_source_effect_stack_without_an_explicit_intermediate_pass()
    {
        using var snapshot = CreateSourceSnapshot(out var sourceId, includeSourceEffects: true);
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = CreateSourceLayerPlan(canvas, output, sourceId);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("no source-effect operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_rejects_layer_effect_stack_without_an_explicit_intermediate_pass()
    {
        using var snapshot = CreateSourceSnapshot(out var sourceId, includeLayerEffects: true);
        var canvas = snapshot.Canvases.Single();
        var output = snapshot.Outputs.Single();
        var plan = CreateSourceLayerPlan(canvas, output, sourceId);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.ValidateFor(snapshot));

        Assert.Contains("enabled layer-effect stack", exception.Message, StringComparison.Ordinal);
    }

    private static RenderFrameSnapshot CreateSnapshot()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var size = new FrameSize(1920, 1080);
        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = size,
                    BackgroundColor = ColorRgba.Black
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program output",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = size,
                    CanvasLayoutMode = LayoutMode.Stretch,
                    LetterboxColor = ColorRgba.Black
                }
            ]
        };
    }

    private static RenderFrameSnapshot CreateTransitionSnapshot()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var size = new FrameSize(1920, 1080);
        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = size,
                    BackgroundColor = ColorRgba.Black
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program output",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    PreviousCanvasId = canvasId,
                    RouteTransitionKind = OutputRouteTransitionKind.Fade,
                    OutputSize = size,
                    CanvasLayoutMode = LayoutMode.Stretch,
                    LetterboxColor = ColorRgba.Black
                }
            ]
        };
    }

    private static RenderFrameSnapshot CreateSourceSnapshot(
        out SourceId sourceId,
        bool includeSourceEffects = false,
        bool includeLayerEffects = false)
    {
        sourceId = SourceId.New();
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var size = new FrameSize(1920, 1080);
        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 2,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = size,
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Camera",
                            SourceId = sourceId,
                            SourceEffects = includeSourceEffects
                                ? [new ColorCorrectionEffectSnapshot { Id = EffectId.New(), Name = "Source grade" }]
                                : [],
                            Effects = includeLayerEffects
                                ? [new BlurEffectSnapshot { Id = EffectId.New(), Name = "Layer blur", Radius = 4f }]
                                : []
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program output",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = size,
                    CanvasLayoutMode = LayoutMode.Stretch,
                    LetterboxColor = ColorRgba.Black
                }
            ]
        };
    }

    private static PhysicalRenderGraphPlan CreateSourceLayerPlan(
        RenderCanvasSnapshot canvas,
        RenderOutputStateSnapshot output,
        SourceId sourceId)
    {
        var sourceLayer = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(Assert.Single(canvas.Objects));
        return new PhysicalRenderGraphPlan(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                Key = "source:camera",
                SourceId = sourceId,
                Consumers = ["source-layer:camera"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderSourceLayer,
                Key = "source-layer:camera",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                DrawObjectId = sourceLayer.Id,
                SourceId = sourceId,
                Dependencies = ["source:camera"],
                Consumers = ["canvas:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                Key = "canvas:program",
                CanvasId = canvas.Id,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["source-layer:camera"],
                Consumers = ["output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                OutputId = output.Id,
                CanvasId = output.CanvasId,
                ResolvedCanvasKey = canvas.PhysicalKey,
                Dependencies = ["canvas:program"]
            }
        ]);
    }

    private static RenderFrameSnapshot CreateNestedCanvasSnapshot(
        out RenderCanvasSnapshot parent,
        out RenderCanvasSnapshot child,
        out RenderOutputStateSnapshot output)
    {
        var parentId = CanvasId.New();
        var childId = CanvasId.New();
        var size = new FrameSize(1920, 1080);
        child = new RenderCanvasSnapshot
        {
            Id = childId,
            Name = "Child",
            Size = size
        };
        parent = new RenderCanvasSnapshot
        {
            Id = parentId,
            Name = "Program",
            Size = size,
            Objects =
            [
                new RenderCanvasDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = "Child layer",
                    NestedCanvasId = childId,
                    NestedCanvas = child,
                    NestedResolvedCanvasKey = child.PhysicalKey
                }
            ]
        };
        output = new RenderOutputStateSnapshot
        {
            Id = RenderOutputId.New(),
            Name = "Program output",
            TypeId = RenderOutputTypes.Offscreen,
            CanvasId = parentId,
            OutputSize = size,
            CanvasLayoutMode = LayoutMode.Stretch,
            LetterboxColor = ColorRgba.Black
        };

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 3,
            Canvases = [parent, child],
            Outputs = [output]
        };
    }
}
