using System.Collections.Immutable;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public class Cp3ChromaKeyEffectTests
{
    [Fact]
    public async Task Text_draw_object_reports_render_drawobject_not_supported()
    {
        var diagnostics = await VulkanCompositionTestHarness.SubmitDrawObjectForDiagnosticsAsync(new RenderTextDrawObjectSnapshot
        {
            Id = DrawObjectId.New(),
            Name = "Title",
            Enabled = true,
            Transform = new Transform2D { Size = new CanvasSize(32, 16) },
            Text = "MediaForge"
        });

        if (diagnostics is null)
            return;

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "render.drawobject_not_supported");
    }
    [Fact]
    public async Task Source_layer_unsupported_effect_reports_render_effect_not_supported()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects: [new BlurEffectSnapshot { Id = EffectId.New(), Name = "Blur" }])
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "render.effect_not_supported");
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Chroma_key_removes_key_color()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            var redHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(1, 0, 0, 1));
            var greenHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context.Device, ColorRgba.From(0, 1, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            redHandle,
                            SourceId.New(),
                            new Transform2D
                            {
                                Position = new CanvasPoint(32, 0),
                                Size = new CanvasSize(32, 64)
                            }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            greenHandle,
                            SourceId.New(),
                            new Transform2D
                            {
                                Position = new CanvasPoint(0, 0),
                                Size = new CanvasSize(32, 64)
                            },
                            effects:
                            [
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Key green",
                                    KeyColor = ColorRgba.From(0, 1, 0, 1),
                                    Similarity = 0.05f,
                                    Smoothness = 0.02f,
                                    SpillReduction = 0f
                                }
                            ])
                    ],
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var keyed));
                VulkanCompositionTestHarness.AssertPixelNear(keyed, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var retained));
                VulkanCompositionTestHarness.AssertPixelNear(retained, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Chroma_key_respects_similarity_smoothness()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(0, 0.8f, 0.2f, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using (var lowSimilarity = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                VulkanCompositionTestHarness.CreateChromaKeyEffect(similarity: 0.01f, smoothness: 0.01f)
                            ])
                    ],
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1)))
                {
                    var submission = backend.Submit(lowSimilarity);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                    Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var retained));
                    VulkanCompositionTestHarness.AssertPixelNear(retained, expectedR: 0, expectedG: 204, expectedB: 51, expectedA: 255, tolerance: 2);
                }

                using (var highSimilarity = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                VulkanCompositionTestHarness.CreateChromaKeyEffect(similarity: 0.35f, smoothness: 0.02f)
                            ])
                    ],
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1)))
                {
                    var submission = backend.Submit(highSimilarity);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                    Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var keyed));
                    VulkanCompositionTestHarness.AssertPixelNear(keyed, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Disabled_effect_is_not_applied()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(0, 1, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Disabled key",
                                    Enabled = false,
                                    KeyColor = ColorRgba.From(0, 1, 0, 1),
                                    Similarity = 1f,
                                    Smoothness = 0.01f
                                }
                            ])
                    ],
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Chroma_key_invalid_configuration_reports_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(0, 1, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Invalid key",
                                    Similarity = float.NaN
                                }
                            ])
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "render.effect_invalid");
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Effect_order_is_preserved()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                new BlurEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Second",
                                    Order = 2
                                },
                                new ColorCorrectionEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "First",
                                    Order = 1
                                }
                            ])
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                var unsupported = diagnostics.Diagnostics
                    .Where(diagnostic => diagnostic.Code == "render.effect_not_supported")
                    .ToArray();

                Assert.Collection(
                    unsupported,
                    first => Assert.Contains(nameof(ColorCorrectionEffectSnapshot), first.Message, StringComparison.Ordinal),
                    second => Assert.Contains(nameof(BlurEffectSnapshot), second.Message, StringComparison.Ordinal));
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Multiple_chroma_key_effects_report_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(0, 1, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            effects:
                            [
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "First key",
                                    KeyColor = ColorRgba.From(0, 1, 0, 1)
                                },
                                new ChromaKeyEffectSnapshot
                                {
                                    Id = EffectId.New(),
                                    Name = "Second key",
                                    KeyColor = ColorRgba.From(0, 0, 1, 1)
                                }
                            ])
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(
                    diagnostics.Diagnostics,
                    diagnostic => diagnostic.Code == "render.effect_not_supported" &&
                                  diagnostic.Message.Contains("Only one active ChromaKeyEffect", StringComparison.Ordinal));
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Add_blend_mode_reports_render_blend_mode_unsupported()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: diagnostics))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            blendMode: BlendMode.Add)
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "render.blend_mode_unsupported");
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
