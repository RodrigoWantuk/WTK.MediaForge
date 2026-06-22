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
public class Cp1OffscreenCompositionTests
{
    [Fact]
    public async Task Cp1_single_source_layer_renders_to_offscreen()
    {
        if (!VulkanCompositionTestHarness.TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        if (!VulkanCompositionTestHarness.TryCreateRenderer(out var context))
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

                backend.BindOutput(new RenderOutputBindingSnapshot
                {
                    OutputId = outputId,
                    TargetKind = RenderTargetKind.Offscreen,
                    SurfaceSize = new FrameSize(1280, 720),
                    BindingVersion = 1
                });

                var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Equal(1, backend.SubmitCount);
                Assert.True(backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(1280u, size.Width);
                Assert.Equal(720u, size.Height);

                snapshot.Dispose();
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_renders_expected_center_pixel()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent);

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
    public async Task Cp1_source_layer_fit_outputs_transparent_outside_content_area()
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
                var size = new FrameSize(128, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(128, 64) },
                    layerLayoutMode: LayoutMode.Fit,
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task SourceLayer_Fit_remains_Fit_when_Output_CanvasLayoutMode_is_Fill()
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
                var size = new FrameSize(128, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(128, 64) },
                    layerLayoutMode: LayoutMode.Fit,
                    outputCanvasLayoutMode: LayoutMode.Fill,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_source_layer_fill_crops_without_transparent_bars()
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
                var size = new FrameSize(128, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(128, 64) },
                    layerLayoutMode: LayoutMode.Fill,
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_source_layer_stretch_fills_entire_box()
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
                var size = new FrameSize(128, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(128, 64) },
                    layerLayoutMode: LayoutMode.Stretch,
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_respects_opacity_on_source_layer()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    opacity: 0.5f,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 128, expectedG: 0, expectedB: 0, expectedA: 128, tolerance: 2);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_letterbox_outputs_letterbox_color()
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 128, 64));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(64, 64),
                    outputSize: new FrameSize(128, 64),
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Output_Fit_letterboxes_canvas()
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 128, 64));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(64, 64),
                    outputSize: new FrameSize(128, 64),
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var letterbox));
                VulkanCompositionTestHarness.AssertPixelNear(letterbox, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 64, 32, out var content));
                VulkanCompositionTestHarness.AssertPixelNear(content, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Output_Fill_crops_canvas()
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 64, 64));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(128, 64),
                    outputSize: new FrameSize(64, 64),
                    transform: new Transform2D { Size = new CanvasSize(128, 64) },
                    outputCanvasLayoutMode: LayoutMode.Fill,
                    outputLetterboxColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var center));
                VulkanCompositionTestHarness.AssertPixelNear(center, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 2, 32, out var edge));
                VulkanCompositionTestHarness.AssertPixelNear(edge, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Output_Stretch_fills_output()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            var redHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(1, 0, 0, 1));
            var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context.Device, ColorRgba.From(0, 0, 1, 1));

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
                                Position = new CanvasPoint(0, 0),
                                Size = new CanvasSize(32, 64)
                            }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            blueHandle,
                            SourceId.New(),
                            new Transform2D
                            {
                                Position = new CanvasPoint(32, 0),
                                Size = new CanvasSize(32, 64)
                            })
                    ],
                    outputCanvasLayoutMode: LayoutMode.Stretch);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var left));
                VulkanCompositionTestHarness.AssertPixelNear(left, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var right));
                VulkanCompositionTestHarness.AssertPixelNear(right, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Source_layer_rotation_reports_unsupported_diagnostic()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, diagnostics: new ListDiagnosticsSink()))
            return;

        using (context)
        {
            var diagnostics = (ListDiagnosticsSink)context!.Diagnostics!;
            VulkanCompositionTestHarness.FillSharedTexture(context.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D
                    {
                        Size = new CanvasSize(64, 64),
                        RotationDegrees = 45f
                    });

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
    public async Task Cp1_canvas_background_color_is_rendered_when_no_layer_covers_pixel()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1),
                    sourceLayerCount: 0);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_transparent_source_layer_preserves_canvas_background()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            VulkanCompositionTestHarness.FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.Transparent);

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

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
    public async Task Cp1_layer_partially_outside_left_is_clipped()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D
                    {
                        Position = new CanvasPoint(-32, 0),
                        Size = new CanvasSize(64, 64)
                    },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var inside));
                VulkanCompositionTestHarness.AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var outside));
                VulkanCompositionTestHarness.AssertPixelNear(outside, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_layer_partially_outside_right_is_clipped()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D
                    {
                        Position = new CanvasPoint(32, 0),
                        Size = new CanvasSize(64, 64)
                    },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var inside));
                VulkanCompositionTestHarness.AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var outside));
                VulkanCompositionTestHarness.AssertPixelNear(outside, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp1_layer_fully_outside_canvas_draws_nothing()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D
                    {
                        Position = new CanvasPoint(80, 0),
                        Size = new CanvasSize(32, 32)
                    },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

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
    public async Task Cp1_negative_position_does_not_trigger_invalid_scissor()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D
                    {
                        Position = new CanvasPoint(-8, -8),
                        Size = new CanvasSize(16, 16)
                    },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 4, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
