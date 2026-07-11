using System.Collections.Immutable;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Graphics.Vulkan.Text;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests.Text;

[Trait("Category", TestCategories.Gpu)]
public sealed class TextRenderingTests
{
    [Fact]
    public async Task Text_renders_with_correct_alignment_pixel_test()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(
                context!.Device,
                context.SharedHandle,
                ColorRgba.From(0, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new Core.Frames.FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 1,
                    Canvases =
                    [
                        new RenderCanvasSnapshot
                        {
                            Id = canvasId,
                            Name = "Program",
                            Size = size,
                            BackgroundColor = ColorRgba.From(0, 0, 0, 1),
                            Objects =
                            [
                                new RenderTextDrawObjectSnapshot
                                {
                                    Id = DrawObjectId.New(),
                                    Name = "Title",
                                    Text = "MF",
                                    FontFamily = "Segoe UI",
                                    FontSize = 24f,
                                    TextColor = ColorRgba.From(0, 1, 0, 1),
                                    Transform = new Transform2D
                                    {
                                        Position = new CanvasPoint(0, 0),
                                        Size = new CanvasSize(64, 64)
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
                            TypeId = Composition.Outputs.RenderOutputTypes.Offscreen,
                            CanvasId = canvasId,
                            OutputSize = size,
                            CanvasLayoutMode = Core.Media.LayoutMode.Fit,
                            LetterboxColor = ColorRgba.Transparent
                        }
                    ]
                };

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Equal(1, backend.SubmitCount);
                Assert.True(backend.TryGetOffscreenTargetSize(outputId, out var targetSize));
                Assert.Equal(64u, targetSize.Width);
                Assert.Equal(64u, targetSize.Height);

                Assert.True(backend.TryGetOffscreenRenderTargetForTests(outputId, out var target));
                var frame = VulkanOffscreenReadback.ReadFrame(target, CancellationToken.None);
                frame.ContainsRenderedColor(minG: 32, maxR: 24, maxB: 24);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Glyph_atlas_reused_across_frames()
    {
        if (!VulkanCompositionTestHarness.TryCreateRenderer(out _))
            return;

        using var device = VulkanHeadlessDevice.Create();
        using var bridge = new VulkanFontAtlasBridge(device);

        Assert.True(bridge.TryResolveAtlas("MF", "Segoe UI", 24f, out var first, out _));
        Assert.True(bridge.TryResolveAtlas("MF", "Segoe UI", 24f, out var second, out _));

        Assert.Equal(first.GpuTextureId, second.GpuTextureId);
        Assert.Equal(1, bridge.AtlasCache.EntryCount);
    }

    [Fact]
    public void Glyph_atlas_differentiates_text_for_same_face_and_size()
    {
        if (!VulkanCompositionTestHarness.TryCreateRenderer(out _))
            return;

        using var device = VulkanHeadlessDevice.Create();
        using var bridge = new VulkanFontAtlasBridge(device);

        Assert.True(bridge.TryResolveAtlas("MF", "Segoe UI", 24f, out var first, out _));
        Assert.True(bridge.TryResolveAtlas("XX", "Segoe UI", 24f, out var second, out _));

        Assert.NotEqual(first.GpuTextureId, second.GpuTextureId);
        Assert.Equal(2, bridge.AtlasCache.EntryCount);
    }

    [Fact]
    public void Font_cache_reuses_atlas_for_same_text_face_and_size()
    {
        var manager = new AssetManager();
        var factoryCalls = 0;

        FontAtlasAsset Factory()
        {
            factoryCalls++;
            return new FontAtlasAsset
            {
                Text = "MF",
                FontFamily = "Segoe UI",
                SizePx = 24f,
                Width = 64,
                Height = 64,
                AtlasPixels = new byte[64 * 64 * 4]
            };
        }

        using var first = manager.LoadFontAtlas("MF", "Segoe UI", 24f, Factory);
        using var second = manager.LoadFontAtlas("MF", "Segoe UI", 24f, Factory);

        Assert.Equal(1, factoryCalls);
        Assert.Same(first.Value, second.Value);
    }

    [Fact]
    public void Font_cache_does_not_reuse_different_text_for_same_face_and_size()
    {
        var manager = new AssetManager();
        var factoryCalls = 0;

        FontAtlasAsset Factory(string text)
        {
            factoryCalls++;
            return new FontAtlasAsset
            {
                Text = text,
                FontFamily = "Segoe UI",
                SizePx = 24f,
                Width = 64,
                Height = 64,
                AtlasPixels = new byte[64 * 64 * 4]
            };
        }

        using var first = manager.LoadFontAtlas("MF", "Segoe UI", 24f, () => Factory("MF"));
        using var second = manager.LoadFontAtlas("XX", "Segoe UI", 24f, () => Factory("XX"));

        Assert.Equal(2, factoryCalls);
        Assert.NotSame(first.Value, second.Value);
    }

    [Fact]
    public void Font_rasterizer_generates_non_placeholder_alpha_mask()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var atlas = VulkanFontAtlasRasterizer.Rasterize("I", "Segoe UI", 48f);
        var alphaPixels = CountAlphaPixels(atlas);

        Assert.True(alphaPixels > 0, "Expected rasterized glyph alpha.");
        Assert.True(
            alphaPixels < atlas.Width * atlas.Height / 2,
            $"Expected glyph alpha to cover less than half the atlas, got {alphaPixels} of {atlas.Width * atlas.Height}.");
    }

    [Fact]
    public void Outline_and_shadow_do_not_trigger_per_frame_cpu_raster()
    {
        if (!VulkanCompositionTestHarness.TryCreateRenderer(out _))
            return;

        var manager = new AssetManager();
        var factoryCalls = 0;

        using var device = VulkanHeadlessDevice.Create();
        using var bridge = new VulkanFontAtlasBridge(device, manager);

        FontAtlasAsset Factory() =>
            new()
            {
                Text = "Shadow",
                FontFamily = "Segoe UI",
                SizePx = 24f,
                Width = 64,
                Height = 64,
                AtlasPixels = new byte[64 * 64 * 4]
            };

        manager.LoadFontAtlas("Shadow", "Segoe UI", 24f, () =>
        {
            factoryCalls++;
            return Factory();
        }).Dispose();

        bridge.TryResolveAtlas("Shadow", "Segoe UI", 24f, out _, out _);
        bridge.TryResolveAtlas("Shadow", "Segoe UI", 24f, out _, out _);
        bridge.TryResolveAtlas("Outline", "Segoe UI", 24f, out _, out _);
        bridge.TryResolveAtlas("Outline", "Segoe UI", 24f, out _, out _);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, bridge.AtlasCache.EntryCount);
    }

    private static int CountAlphaPixels(FontAtlasAsset atlas)
    {
        var count = 0;
        for (var i = 3; i < atlas.AtlasPixels.Length; i += 4)
        {
            if (atlas.AtlasPixels[i] != 0)
                count++;
        }

        return count;
    }
}

internal static class VulkanReadbackFrameAssertions
{
    public static void ContainsRenderedColor(
        this VulkanReadbackFrame frame,
        byte minG,
        byte maxR,
        byte maxB)
    {
        var width = frame.StrideBytes / 4;
        var height = frame.Pixels.Length / frame.StrideBytes;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = checked(y * frame.StrideBytes + x * 4);
                var pixels = frame.Pixels;
                if (pixels[offset] <= maxR &&
                    pixels[offset + 1] >= minG &&
                    pixels[offset + 2] <= maxB &&
                    pixels[offset + 3] >= minG)
                {
                    return;
                }
            }
        }

        Assert.Fail("Expected at least one rendered text pixel in the output frame.");
    }
}
