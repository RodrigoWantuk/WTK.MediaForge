using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class CanvasEffectMaskVulkanTests
{
    [Fact]
    public async Task Canvas_effect_rectangle_mask_composites_effect_only_inside_its_roi()
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
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);
                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 1,
                    Canvases =
                    [
                        new RenderCanvasSnapshot
                        {
                            Id = canvasId,
                            Name = "Masked canvas effect",
                            Size = size,
                            BackgroundColor = ColorRgba.Transparent,
                            Objects =
                            [
                                new RenderSolidDrawObjectSnapshot
                                {
                                    Id = DrawObjectId.New(),
                                    Name = "Dark green",
                                    Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                                    FillColor = ColorRgba.From(0f, 0.2f, 0f, 1f)
                                }
                            ],
                            Effects =
                            [
                                new ColorCorrectionEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Brighten left half",
                                    Brightness = 0.3f,
                                    Contrast = 1f,
                                    Saturation = 1f,
                                    Mask = new RectangleEffectMaskStateSnapshot
                                    {
                                        Bounds = new NormalizedRect(0f, 0f, 0.5f, 1f)
                                    }
                                }
                            ]
                        }
                    ],
                    Outputs =
                    [
                        new RenderOutputStateSnapshot
                        {
                            Id = outputId,
                            Name = "Offscreen",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = canvasId,
                            OutputSize = size,
                            CanvasLayoutMode = LayoutMode.Stretch,
                            LetterboxColor = ColorRgba.Transparent
                        }
                    ]
                };

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var affected));
                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var unaffected));
                VulkanCompositionTestHarness.AssertPixelNear(affected, expectedR: 76, expectedG: 128, expectedB: 76, expectedA: 255, tolerance: 5);
                VulkanCompositionTestHarness.AssertPixelNear(unaffected, expectedR: 0, expectedG: 51, expectedB: 0, expectedA: 255, tolerance: 5);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
