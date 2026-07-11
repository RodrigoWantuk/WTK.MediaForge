using System.Collections.Immutable;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class Cp5BlurEffectTests
{
    [Fact]
    public async Task Blur_effect_spreads_source_pixels_to_neighboring_area()
    {
        var result = await RenderBlurredSquareAsync(radius: 8f);
        if (result is null)
            return;

        var (backend, outputId, context) = result.Value;
        using (context)
        {
            try
            {
                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var center));
                Assert.True(backend.TryReadOffscreenPixel(outputId, 26, 32, out var blurredEdge));
                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 4, out var farCorner));

                Assert.True(center.R > 20, $"Expected red center to remain visible after blur, got {center}.");
                Assert.True(blurredEdge.R > 8, $"Expected blur to spread red into neighboring transparent area, got {blurredEdge}.");
                Assert.True(center.R > blurredEdge.R, $"Expected blur center to remain stronger than edge, got center {center} and edge {blurredEdge}.");
                VulkanCompositionTestHarness.AssertPixelNear(farCorner, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                context.Guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Repeated_blur_submits_reuse_intermediate_targets()
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
                PrepareSourceSquare(context.Device, context.SharedHandle);

                using var snapshot = CreateBlurSnapshot(context.SharedHandle, canvasId, outputId, size, radius: 6f);

                int? firstPoolCount = null;
                for (var i = 0; i < 20; i++)
                {
                    var submission = backend.Submit(snapshot);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                    firstPoolCount ??= backend.IntermediateTargetPoolLiveCountForTests;
                    Assert.Equal(firstPoolCount.Value, backend.IntermediateTargetPoolLiveCountForTests);
                }

                Assert.Equal(4, firstPoolCount.GetValueOrDefault());
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static async Task<(MediaForgeVulkanRenderer Backend, RenderOutputId OutputId, VulkanCompositionTestHarness.CompositionTestContext Context)?>
        RenderBlurredSquareAsync(float radius)
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return null;

        var guard = context!.Guard;
        var backend = context.Backend;
        guard.BindToCurrentThread();

        try
        {
            var outputId = RenderOutputId.New();
            var canvasId = CanvasId.New();
            var size = new FrameSize(64, 64);

            backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
            PrepareSourceSquare(context.Device, context.SharedHandle);

            using var snapshot = CreateBlurSnapshot(context.SharedHandle, canvasId, outputId, size, radius);
            var submission = backend.Submit(snapshot);
            await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

            return (backend, outputId, context);
        }
        catch
        {
            guard.Clear();
            context.Dispose();
            throw;
        }
    }

    private static void PrepareSourceSquare(
        D3D11GpuDevice device,
        D3D11SharedTextureFrameHandle handle)
    {
        VulkanCompositionTestHarness.FillSharedTexture(device, handle, ColorRgba.Transparent);
        VulkanCompositionTestHarness.FillSharedTextureRegion(
            device,
            handle,
            dstX: 28,
            dstY: 28,
            regionWidth: 8,
            regionHeight: 8,
            ColorRgba.From(1, 0, 0, 1));
    }

    private static RenderFrameSnapshot CreateBlurSnapshot(
        D3D11SharedTextureFrameHandle handle,
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize size,
        float radius) =>
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
                    effects: ImmutableArray.Create<EffectStateSnapshot>(
                        new BlurEffectSnapshot
                        {
                            Id = EffectId.New(),
                            Name = "Soft blur",
                            Radius = radius
                        }))
            ],
            canvasBackgroundColor: ColorRgba.Black,
            outputLetterboxColor: ColorRgba.Black);
}
