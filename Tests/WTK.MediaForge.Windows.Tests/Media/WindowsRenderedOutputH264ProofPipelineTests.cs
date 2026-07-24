using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Windows.Media.Proofs;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsRenderedOutputH264ProofPipelineTests
{
    [Fact]
    public void Product_proof_executes_physical_render_graph_before_submission()
    {
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        using var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Context = new RenderFrameContext(
                7,
                TimeSpan.FromSeconds(0.1),
                TimeSpan.FromSeconds(1d / 60d),
                60,
                CancellationToken.None),
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Proof canvas",
                    Size = new FrameSize(320, 180),
                    BackgroundColor = ColorRgba.Black
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Proof output",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(320, 180)
                }
            ]
        };

        WindowsPhysicalRenderProofPreparation.Execute(snapshot, outputId);

        Assert.NotNull(snapshot.RenderGraphExecution);
        Assert.NotEmpty(snapshot.RenderGraphExecution.PhysicalPlan.Operations);
        Assert.Contains(
            snapshot.RenderGraphExecution.PhysicalPlan.Operations,
            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderCanvas);
        Assert.Contains(
            snapshot.RenderGraphExecution.PhysicalPlan.Operations,
            operation => operation.Kind == PhysicalRenderGraphOperationKind.RenderOutput);
    }
}
