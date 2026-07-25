using WTK.MediaForge.Composition.Outputs;
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
}
