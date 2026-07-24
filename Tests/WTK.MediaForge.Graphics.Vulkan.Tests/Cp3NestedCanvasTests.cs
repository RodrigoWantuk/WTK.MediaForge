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
public class Cp3NestedCanvasTests
{
    [Fact]
    public async Task Nested_canvas_renders_into_parent()
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
                var parentId = CanvasId.New();
                var child = VulkanCompositionTestHarness.CreateSolidCanvas(
                    CanvasId.New(),
                    new FrameSize(32, 32),
                    ColorRgba.From(1, 0, 0, 1));
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    parentId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Child canvas",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            NestedCanvas = child
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Nested_canvas_respects_transform()
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
                var parentId = CanvasId.New();
                var child = VulkanCompositionTestHarness.CreateSolidCanvas(
                    CanvasId.New(),
                    new FrameSize(16, 16),
                    ColorRgba.From(0, 0, 1, 1));
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    parentId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Positioned child",
                            Transform = new Transform2D
                            {
                                Position = new CanvasPoint(16, 16),
                                Size = new CanvasSize(16, 16)
                            },
                            NestedCanvas = child
                        }
                    ],
                    canvasBackgroundColor: ColorRgba.Transparent,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 24, 24, out var inside));
                VulkanCompositionTestHarness.AssertPixelNear(inside, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 8, 8, out var outside));
                VulkanCompositionTestHarness.AssertPixelNear(outside, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Nested_canvas_depth_8_works()
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
                var parentId = CanvasId.New();
                var size = new FrameSize(32, 32);
                var nested = VulkanCompositionTestHarness.CreateNestedCanvasChain(depth: 8, size, ColorRgba.From(0, 1, 0, 1));

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    parentId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Depth 1",
                            Transform = new Transform2D { Size = new CanvasSize(32, 32) },
                            NestedCanvas = nested
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 16, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Nested_canvas_target_lifetime_survives_submission()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        VulkanOffscreenRenderTargetLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var parentId = CanvasId.New();
                var size = new FrameSize(32, 32);
                var child = VulkanCompositionTestHarness.CreateSolidCanvas(
                    CanvasId.New(),
                    size,
                    ColorRgba.From(1, 0, 0, 1));

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    parentId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Child canvas",
                            Transform = new Transform2D { Size = new CanvasSize(32, 32) },
                            NestedCanvas = child
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                Assert.True(VulkanOffscreenRenderTargetLifetime.LiveCount >= 3);

                submission.DisposeCompleted();

                Assert.Equal(3, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(2, backend.IntermediateTargetPoolLiveCountForTests);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Canvas_draw_object_does_not_report_render_drawobject_not_supported()
    {
        var diagnostics = await VulkanCompositionTestHarness.SubmitDrawObjectForDiagnosticsAsync(new RenderCanvasDrawObjectSnapshot
        {
            Id = DrawObjectId.New(),
            Name = "Nested",
            Enabled = true,
            Transform = new Transform2D { Size = new CanvasSize(32, 32) },
            NestedCanvas = new RenderCanvasSnapshot
            {
                Id = CanvasId.New(),
                Name = "Child",
                Size = new FrameSize(32, 32),
                Objects = []
            }
        });

        if (diagnostics is null)
            return;

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "render.drawobject_not_supported");
    }
    [Fact]
    public async Task Canvas_layer_rotation_is_supported_without_unsupported_diagnostic()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: new ListDiagnosticsSink()))
            return;

        using (context)
        {
            var diagnostics = (ListDiagnosticsSink)context!.Diagnostics!;
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var mainCanvasId = CanvasId.New();
                var nestedCanvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    mainCanvasId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Nested canvas",
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(64, 64),
                                RotationDegrees = 90f
                            },
                            NestedCanvas = new RenderCanvasSnapshot
                            {
                                Id = nestedCanvasId,
                                Name = "Child",
                                Size = size,
                                Objects =
                                [
                                    VulkanCompositionTestHarness.CreateSourceLayer(context.SharedHandle, SourceId.New(), new Transform2D
                                    {
                                        Size = new CanvasSize(64, 64)
                                    })
                                ]
                            }
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.DoesNotContain(
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
    public async Task Canvas_layer_crop_is_supported_without_unsupported_diagnostic()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: new ListDiagnosticsSink()))
            return;

        using (context)
        {
            var diagnostics = (ListDiagnosticsSink)context!.Diagnostics!;
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var mainCanvasId = CanvasId.New();
                var nestedCanvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateObjectSnapshot(
                    mainCanvasId,
                    outputId,
                    size,
                    size,
                    [
                        new RenderCanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Nested canvas",
                            Transform = new Transform2D { Size = new CanvasSize(64, 64) },
                            EffectiveCrop = new NormalizedRect(0.2f, 0.2f, 0.8f, 0.8f),
                            NestedCanvas = new RenderCanvasSnapshot
                            {
                                Id = nestedCanvasId,
                                Name = "Child",
                                Size = size,
                                Objects =
                                [
                                    VulkanCompositionTestHarness.CreateSourceLayer(context.SharedHandle, SourceId.New(), new Transform2D
                                    {
                                        Size = new CanvasSize(64, 64)
                                    })
                                ]
                            }
                        }
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.DoesNotContain(
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
