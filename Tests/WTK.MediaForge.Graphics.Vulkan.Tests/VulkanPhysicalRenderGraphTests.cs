using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class VulkanPhysicalRenderGraphTests
{
    [Fact]
    public async Task Same_canvas_routed_to_two_outputs_renders_canvas_once_and_fans_out_output_passes()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var canvasId = CanvasId.New();
                var fullOutputId = RenderOutputId.New();
                var halfOutputId = RenderOutputId.New();
                var canvasSize = new FrameSize(64, 64);
                var fullOutputSize = new FrameSize(64, 64);
                var halfOutputSize = new FrameSize(32, 32);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(
                    fullOutputId,
                    fullOutputSize.Width,
                    fullOutputSize.Height));
                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(
                    halfOutputId,
                    halfOutputSize.Width,
                    halfOutputSize.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 1,
                    Canvases =
                    [
                        VulkanCompositionTestHarness.CreateSolidCanvas(
                            canvasId,
                            canvasSize,
                            ColorRgba.From(0, 0.8f, 0.2f, 1))
                    ],
                    Outputs =
                    [
                        new RenderOutputStateSnapshot
                        {
                            Id = fullOutputId,
                            Name = "Program full",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = canvasId,
                            OutputSize = fullOutputSize,
                            CanvasLayoutMode = LayoutMode.Stretch,
                            LetterboxColor = ColorRgba.Black
                        },
                        new RenderOutputStateSnapshot
                        {
                            Id = halfOutputId,
                            Name = "Program half",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = canvasId,
                            OutputSize = halfOutputSize,
                            CanvasLayoutMode = LayoutMode.Stretch,
                            LetterboxColor = ColorRgba.Black
                        }
                    ]
                };

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(fullOutputId, 32, 32, out var fullPixel));
                Assert.True(backend.TryReadOffscreenPixel(halfOutputId, 16, 16, out var halfPixel));
                VulkanCompositionTestHarness.AssertPixelNear(fullPixel, expectedR: 0, expectedG: 204, expectedB: 51, expectedA: 255, tolerance: 3);
                VulkanCompositionTestHarness.AssertPixelNear(halfPixel, expectedR: 0, expectedG: 204, expectedB: 51, expectedA: 255, tolerance: 3);

                var stats = backend.LastPhysicalCompositionStatsForTests;
                Assert.Equal(1, stats.CanvasRenderPasses);
                Assert.Equal(1, stats.ReusedCanvasPasses);
                Assert.Equal(2, stats.OutputCompositePasses);
                Assert.Equal(0, stats.TransitionPasses);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
