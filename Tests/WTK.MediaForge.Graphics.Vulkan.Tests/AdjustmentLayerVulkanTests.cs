using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class AdjustmentLayerVulkanTests
{
    [Fact]
    public async Task Adjustment_layer_applies_color_correction_to_layers_below_and_preserves_later_layers()
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

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Dark green below",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0f, 0.2f, 0f, 1f)
                        },
                        new RenderAdjustmentLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Brighten below",
                            TargetMode = AdjustmentLayerTargetMode.LayersBelow,
                            Effects =
                            [
                                new ColorCorrectionEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Brightness",
                                    Brightness = 0.3f,
                                    Contrast = 1f,
                                    Saturation = 1f
                                }
                            ]
                        },
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Blue above",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0f, 0f, 1f, 1f),
                            Opacity = 0.5f
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 38, expectedG: 64, expectedB: 166, expectedA: 255, tolerance: 5);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Adjustment_layer_rectangle_mask_limits_effect_to_its_roi()
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

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Dark green below",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0f, 0.2f, 0f, 1f)
                        },
                        new RenderAdjustmentLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Brighten left half",
                            TargetMode = AdjustmentLayerTargetMode.LayersBelow,
                            Mask = new RectangleEffectMaskStateSnapshot
                            {
                                Bounds = new NormalizedRect(0f, 0f, 0.5f, 1f),
                                Opacity = 1f
                            },
                            Effects =
                            [
                                new ColorCorrectionEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Brightness",
                                    Brightness = 0.3f,
                                    Contrast = 1f,
                                    Saturation = 1f
                                }
                            ]
                        }
                    ]);

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

    [Theory]
    [InlineData("rounded")]
    [InlineData("ellipse")]
    public async Task Adjustment_layer_geometric_masks_apply_inversion_and_opacity(string shape)
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
                EffectMaskStateSnapshot mask = shape switch
                {
                    "rounded" => new RoundedRectangleEffectMaskStateSnapshot { CornerRadius = 0.5f, Invert = true, Opacity = 0.5f },
                    "ellipse" => new EllipseEffectMaskStateSnapshot { Invert = true, Opacity = 0.5f },
                    _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown geometric mask test case.")
                };

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Dark green below",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0f, 0.2f, 0f, 1f)
                        },
                        new RenderAdjustmentLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = $"Inverted {shape} mask",
                            TargetMode = AdjustmentLayerTargetMode.LayersBelow,
                            Mask = mask,
                            Effects =
                            [
                                new ColorCorrectionEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Brightness",
                                    Brightness = 0.3f,
                                    Contrast = 1f,
                                    Saturation = 1f
                                }
                            ]
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var center));
                Assert.True(backend.TryReadOffscreenPixel(outputId, 2, 2, out var corner));
                VulkanCompositionTestHarness.AssertPixelNear(center, expectedR: 0, expectedG: 51, expectedB: 0, expectedA: 255, tolerance: 5);
                VulkanCompositionTestHarness.AssertPixelNear(corner, expectedR: 38, expectedG: 89, expectedB: 38, expectedA: 255, tolerance: 5);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
