using System.Collections.Immutable;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public sealed class Cp4ColorCorrectionEffectTests
{
    [Fact]
    public async Task Color_correction_brightness_increases_rgb()
    {
        var pixel = await RenderColorCorrectionAsync(
            ColorRgba.From(0.2f, 0.2f, 0.2f, 1f),
            new ColorCorrectionEffectSnapshot
            {
                Id = EffectId.New(),
                Name = "Brighten",
                Brightness = 0.3f,
                Contrast = 1f,
                Saturation = 1f
            });

        if (pixel is null)
            return;

        VulkanCompositionTestHarness.AssertPixelNear(pixel.Value, expectedR: 128, expectedG: 128, expectedB: 128, expectedA: 255, tolerance: 4);
    }

    [Fact]
    public async Task Color_correction_contrast_expands_from_midpoint()
    {
        var pixel = await RenderColorCorrectionAsync(
            ColorRgba.From(0.25f, 0.5f, 0.75f, 1f),
            new ColorCorrectionEffectSnapshot
            {
                Id = EffectId.New(),
                Name = "Contrast",
                Contrast = 2f,
                Saturation = 1f
            });

        if (pixel is null)
            return;

        VulkanCompositionTestHarness.AssertPixelNear(pixel.Value, expectedR: 0, expectedG: 128, expectedB: 255, expectedA: 255, tolerance: 4);
    }

    [Fact]
    public async Task Color_correction_saturation_zero_outputs_grayscale()
    {
        var pixel = await RenderColorCorrectionAsync(
            ColorRgba.From(1f, 0f, 0f, 1f),
            new ColorCorrectionEffectSnapshot
            {
                Id = EffectId.New(),
                Name = "Desaturate",
                Contrast = 1f,
                Saturation = 0.0001f
            });

        if (pixel is null)
            return;

        VulkanCompositionTestHarness.AssertPixelNear(pixel.Value, expectedR: 54, expectedG: 54, expectedB: 54, expectedA: 255, tolerance: 4);
    }

    [Fact]
    public async Task Color_correction_hue_rotates_red_toward_green()
    {
        var pixel = await RenderColorCorrectionAsync(
            ColorRgba.From(1f, 0f, 0f, 1f),
            new ColorCorrectionEffectSnapshot
            {
                Id = EffectId.New(),
                Name = "Hue",
                Contrast = 1f,
                Saturation = 1f,
                HueDegrees = -120f
            });

        if (pixel is null)
            return;

        Assert.True(pixel.Value.G > 150, $"Expected green channel to dominate after hue rotation, got {pixel.Value}.");
        Assert.True(pixel.Value.R < 32, $"Expected red channel to drop after hue rotation, got {pixel.Value}.");
        Assert.True(pixel.Value.B < 32, $"Expected blue channel to drop after hue rotation, got {pixel.Value}.");
    }

    [Fact]
    public async Task Disabled_color_correction_keeps_source_pixel()
    {
        var pixel = await RenderColorCorrectionAsync(
            ColorRgba.From(0.1f, 0.2f, 0.3f, 1f),
            new ColorCorrectionEffectSnapshot
            {
                Id = EffectId.New(),
                Name = "Disabled",
                Enabled = false,
                Brightness = 0.8f,
                Contrast = 4f,
                Saturation = 0.5f,
                HueDegrees = 90f
            });

        if (pixel is null)
            return;

        VulkanCompositionTestHarness.AssertPixelNear(pixel.Value, expectedR: 26, expectedG: 51, expectedB: 77, expectedA: 255, tolerance: 4);
    }

    [Fact]
    public async Task Color_correction_invalid_configuration_reports_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1f, 0f, 0f, 1f));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateSnapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    size,
                    [
                        new ColorCorrectionEffectSnapshot
                        {
                            Id = EffectId.New(),
                            Name = "Invalid",
                            Contrast = 0f,
                            Saturation = 1f
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(
                    diagnostics.Diagnostics,
                    diagnostic => diagnostic.Code == "render.effect_invalid" &&
                                  diagnostic.Message.Contains("Contrast", StringComparison.Ordinal));
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static async Task<VulkanReadbackPixel?> RenderColorCorrectionAsync(
        ColorRgba sourceColor,
        ColorCorrectionEffectSnapshot effect)
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return null;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, sourceColor);

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateSnapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    size,
                    [effect]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                return backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel)
                    ? pixel
                    : null;
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static RenderFrameSnapshot CreateSnapshot(
        D3D11SharedTextureFrameHandle handle,
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize size,
        ImmutableArray<EffectStateSnapshot> effects) =>
        VulkanCompositionTestHarness.CreateCp2Snapshot(
            canvasId,
            outputId,
            size,
            size,
            [
                new VulkanCompositionTestHarness.Cp2LayerSpec(
                    handle,
                    SourceId.New(),
                    new Transform2D { Size = new CanvasSize(size.Width, size.Height) },
                    effects: effects)
            ]);
}
