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
public class Cp3SolidLayerTests
{
    [Fact]
    public async Task Solid_layer_renders_expected_color()
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
                            Name = "Green",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0, 1, 0, 1)
                        }
                    ]);

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
    public async Task Solid_layer_blends_over_source_layer()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        VulkanCompositionTestHarness.CreateSourceLayer(context.SharedHandle, SourceId.New(), new Transform2D
                        {
                            Size = new CanvasSize(64, 64)
                        }),
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Blue overlay",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0, 0, 1, 1),
                            Opacity = 0.5f
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 128, expectedG: 0, expectedB: 128, expectedA: 255, tolerance: 3);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Solid_layer_respects_transform_and_clipping()
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
                            Name = "Clipped red",
                            Transform = new Transform2D
                            {
                                Position = new CanvasPoint(48, 48),
                                Size = new CanvasSize(32, 32)
                            },
                            FillColor = ColorRgba.From(1, 0, 0, 1)
                        }
                    ],
                    canvasBackgroundColor: ColorRgba.Transparent,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 56, 56, out var inside));
                VulkanCompositionTestHarness.AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 40, 40, out var outside));
                VulkanCompositionTestHarness.AssertPixelNear(outside, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Solid_layer_opacity_0_5_blends_exactly_once_over_source_layer()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        VulkanCompositionTestHarness.CreateSourceLayer(context.SharedHandle, SourceId.New(), new Transform2D
                        {
                            Size = new CanvasSize(64, 64)
                        }),
                        new RenderSolidDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Blue overlay",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            FillColor = ColorRgba.From(0, 0, 1, 1),
                            Opacity = 0.5f
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 128, expectedG: 0, expectedB: 128, expectedA: 255, tolerance: 2);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Solid_draw_object_does_not_report_render_drawobject_not_supported()
    {
        var diagnostics = await VulkanCompositionTestHarness.SubmitDrawObjectForDiagnosticsAsync(new RenderSolidDrawObjectSnapshot
        {
            Id = DrawObjectId.New(),
            Name = "Solid",
            Enabled = true,
            Transform = new Transform2D { Size = new CanvasSize(32, 32) },
            FillColor = ColorRgba.White
        });

        if (diagnostics is null)
            return;

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "render.drawobject_not_supported");
    }
    [Fact]
    public async Task Solid_layer_rotation_reports_unsupported_diagnostic()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: new ListDiagnosticsSink()))
            return;

        using (context)
        {
            var diagnostics = (ListDiagnosticsSink)context!.Diagnostics!;
            var guard = context.Guard;
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
                            Name = "Rotated solid",
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(64, 64),
                                RotationDegrees = 15f
                            },
                            FillColor = ColorRgba.From(0, 0, 1, 1)
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(
                    diagnostics.Diagnostics,
                    diagnostic => diagnostic.Code == "render.transform_rotation_unsupported");
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Solid_layer_crop_reports_unsupported_diagnostic()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: new ListDiagnosticsSink()))
            return;

        using (context)
        {
            var diagnostics = (ListDiagnosticsSink)context!.Diagnostics!;

            var guard = context.Guard;
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
                            Name = "Cropped solid",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            EffectiveCrop = new NormalizedRect(0.1f, 0.1f, 0.9f, 0.9f),
                            FillColor = ColorRgba.From(0, 0, 1, 1)
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Contains(
                    diagnostics.Diagnostics,
                    diagnostic => diagnostic.Code == "render.crop_unsupported");
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
