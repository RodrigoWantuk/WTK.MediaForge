using System.Collections.Immutable;
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
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanCp1")]
public class Cp1OffscreenCompositionTests
{
    [Fact]
    public async Task Cp1_single_source_layer_renders_to_offscreen()
    {
        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        if (!TryCreateRenderer(out var context))
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

                var snapshot = CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

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
    public async Task Offscreen_target_survives_unbind_until_submission_fence_completes()
    {
        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        VulkanOffscreenRenderTargetLifetime.Reset();

        if (!TryCreateRenderer(out var context))
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
                    SurfaceSize = new FrameSize(640, 480),
                    BindingVersion = 1
                });

                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);

                var snapshot = CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);

                backend.UnbindOutput(outputId);

                Assert.Equal(0, backend.OffscreenTargetCount);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(0, VulkanOffscreenRenderTargetLifetime.DisposeCount);

                await ReleaseSubmissionAsync(submission);

                Assert.Equal(0, backend.TextureRegistryActiveLeaseCount);
                Assert.Equal(0, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.DisposeCount);

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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp2_same_source_two_layers_render_at_different_positions()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var sourceId = SourceId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new Cp2LayerSpec(
                            context.SharedHandle,
                            sourceId,
                            new Transform2D
                            {
                                Position = new CanvasPoint(0, 0),
                                Size = new CanvasSize(16, 16)
                            }),
                        new Cp2LayerSpec(
                            context.SharedHandle,
                            sourceId,
                            new Transform2D
                            {
                                Position = new CanvasPoint(32, 32),
                                Size = new CanvasSize(16, 16)
                            })
                    ]);

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 8, 8, out var first));
                AssertPixelNear(first, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 40, 40, out var second));
                AssertPixelNear(second, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 24, 24, out var gap));
                AssertPixelNear(gap, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(128, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(128, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(128, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(128, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    opacity: 0.5f,
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 128, expectedG: 0, expectedB: 0, expectedA: 128, tolerance: 2);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 128, 64));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(64, 64),
                    outputSize: new FrameSize(128, 64),
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputCanvasLayoutMode: LayoutMode.Fit,
                    outputLetterboxColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
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

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 0, 1, 1),
                    sourceLayerCount: 0);

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.Transparent);

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent,
                    canvasBackgroundColor: ColorRgba.From(0, 1, 0, 1));

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var inside));
                AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var outside));
                AssertPixelNear(outside, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 48, 32, out var inside));
                AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 32, out var outside));
                AssertPixelNear(outside, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                AssertPixelNear(pixel, expectedR: 0, expectedG: 255, expectedB: 0, expectedA: 255);
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
        if (!TryCreateCp1Context(out var context))
            return;

        using (context)
        {
            FillSharedTexture(context!.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));

            var guard = context.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = CreateCp1Snapshot(
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
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 4, 4, out var pixel));
                AssertPixelNear(pixel, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Framebuffer_is_not_destroyed_before_submission_completes()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);

                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);

                submission.DisposeCompleted();

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Descriptor_sets_are_released_after_submission_completes()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.FreedDescriptorSets);

                await ReleaseSubmissionAsync(submission);

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Repeated_cp1_submits_do_not_exhaust_descriptor_pool()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                for (var i = 0; i < 50; i++)
                {
                    using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                    var submission = backend.Submit(snapshot);
                    await ReleaseSubmissionAsync(submission);
                }

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(100, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
                Assert.Equal(100, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp1_many_layers_does_not_exhaust_descriptor_pool()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 128, 128));

                using var snapshot = CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(128, 128),
                    outputSize: new FrameSize(128, 128),
                    transform: new Transform2D { Size = new CanvasSize(128, 128) },
                    outputLetterboxColor: ColorRgba.Transparent,
                    sourceLayerCount: 40);

                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(41, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp1_submission_dispose_releases_framebuffers_and_descriptors()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveDescriptorSets);

                await ReleaseSubmissionAsync(submission);

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp1_source_import_layout_remains_ShaderReadOnly_after_successful_submit()
    {
        if (!TryCreateCp1Context(out var context))
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

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                using var lease = backend.TextureRegistry.Acquire(context.SharedHandle);
                Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp1_second_submit_uses_ShaderReadOnly_to_ShaderReadOnly_or_skips_transition()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanImageLayoutTransitionLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using (var firstSnapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId))
                {
                    var first = backend.Submit(firstSnapshot);
                    await ReleaseSubmissionAsync(first);
                }

                using (var lease = backend.TextureRegistry.Acquire(context.SharedHandle))
                    Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);

                using (var secondSnapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId))
                {
                    var second = backend.Submit(secondSnapshot);
                    await ReleaseSubmissionAsync(second);
                }

                using (var lease = backend.TextureRegistry.Acquire(context.SharedHandle))
                    Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);

                Assert.Equal(1, VulkanImageLayoutTransitionLifetime.UndefinedToShaderReadTransitions);
                Assert.Equal(0, VulkanImageLayoutTransitionLifetime.GeneralToShaderReadTransitions);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Cp1_output_target_transitions_to_ShaderReadOnly_after_render_pass()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanImageLayoutTransitionLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryGetOffscreenTargetLayout(outputId, out var layout));
                Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, layout);
                Assert.Equal(2, VulkanImageLayoutTransitionLifetime.ColorAttachmentToShaderReadTransitions);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void QueueSubmit_failure_rolls_back_output_target_layout()
    {
        var faultInjector = new TestVulkanRendererFaultInjector { FailQueueSubmit = true };

        if (!TryCreateCp1Context(out var context, faultInjector))
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

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));
                Assert.True(backend.TryGetOffscreenTargetLayout(outputId, out var initialLayout));
                Assert.Equal(ImageLayout.Undefined, initialLayout);

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);

                Assert.Throws<InvalidOperationException>(() => backend.Submit(snapshot));

                Assert.True(backend.TryGetOffscreenTargetLayout(outputId, out var rollbackLayout));
                Assert.Equal(ImageLayout.Undefined, rollbackLayout);
                Assert.Equal(0, backend.TextureRegistryActiveLeaseCount);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Submission_disposes_descriptor_sets_before_texture_leases()
    {
        if (!TryCreateCp1Context(out var context))
            return;

        VulkanSubmissionResourceLifetime.Reset();

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                submission.DisposeCompleted();

                Assert.True(VulkanSubmissionResourceLifetime.LastDescriptorSetFreeOrder > 0);
                Assert.True(VulkanSubmissionResourceLifetime.FirstTextureLeaseDisposeOrder > 0);
                Assert.True(
                    VulkanSubmissionResourceLifetime.LastDescriptorSetFreeOrder <
                    VulkanSubmissionResourceLifetime.FirstTextureLeaseDisposeOrder);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static RenderFrameSnapshot CreateCp1Snapshot(
        D3D11SharedTextureFrameHandle sharedHandle,
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize? canvasSize = null,
        FrameSize? outputSize = null,
        Transform2D? transform = null,
        LayoutMode layerLayoutMode = LayoutMode.Fit,
        LayoutMode outputCanvasLayoutMode = LayoutMode.Fit,
        ColorRgba? outputLetterboxColor = null,
        ColorRgba? canvasBackgroundColor = null,
        float opacity = 1f,
        int sourceLayerCount = 1)
    {
        if (sourceLayerCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceLayerCount));

        var resolvedCanvasSize = canvasSize ?? new FrameSize(1920, 1080);
        var resolvedOutputSize = outputSize ?? new FrameSize(1280, 720);
        var resolvedTransform = transform ?? new Transform2D
        {
            Size = new CanvasSize(resolvedCanvasSize.Width, resolvedCanvasSize.Height)
        };
        var frame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = sharedHandle,
            TextureSize = sharedHandle.TextureSize,
            LogicalSize = sharedHandle.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };
        var objects = Enumerable
            .Range(0, sourceLayerCount)
            .Select(_ => new RenderSourceLayerDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Desktop",
                SourceId = frame.SourceId,
                Transform = resolvedTransform,
                LayoutMode = layerLayoutMode,
                Opacity = opacity,
                BoundFrame = frame
            })
            .Cast<RenderDrawObjectSnapshot>()
            .ToImmutableArray();

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = resolvedCanvasSize,
                    BackgroundColor = canvasBackgroundColor ?? ColorRgba.Transparent,
                    Objects = objects
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
                    OutputSize = resolvedOutputSize,
                    CanvasLayoutMode = outputCanvasLayoutMode,
                    LetterboxColor = outputLetterboxColor ?? ColorRgba.Black
                }
            ]
        };
    }

    private static RenderFrameSnapshot CreateCp2Snapshot(
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize canvasSize,
        FrameSize outputSize,
        IReadOnlyList<Cp2LayerSpec> layers,
        ColorRgba? canvasBackgroundColor = null,
        ColorRgba? outputLetterboxColor = null,
        LayoutMode outputCanvasLayoutMode = LayoutMode.Fit)
    {
        var objects = layers
            .Select((layer, index) =>
            {
                var frame = new GpuFrameReference
                {
                    Backend = GpuFrameBackend.D3D11SharedTexture,
                    Handle = layer.Handle,
                    TextureSize = layer.Handle.TextureSize,
                    LogicalSize = layer.Handle.TextureSize,
                    SourceId = layer.SourceId,
                    FrameNumber = index + 1
                };

                return (RenderDrawObjectSnapshot)new RenderSourceLayerDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = $"Layer {index + 1}",
                    Enabled = layer.Enabled,
                    SourceId = layer.SourceId,
                    Transform = layer.Transform,
                    LayoutMode = layer.LayoutMode,
                    Opacity = layer.Opacity,
                    BlendMode = layer.BlendMode,
                    BoundFrame = frame
                };
            })
            .ToImmutableArray();

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 2,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = canvasSize,
                    BackgroundColor = canvasBackgroundColor ?? ColorRgba.Transparent,
                    Objects = objects
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
                    OutputSize = outputSize,
                    CanvasLayoutMode = outputCanvasLayoutMode,
                    LetterboxColor = outputLetterboxColor ?? ColorRgba.Transparent
                }
            ]
        };
    }

    private readonly struct Cp2LayerSpec
    {
        public Cp2LayerSpec(
            D3D11SharedTextureFrameHandle handle,
            SourceId sourceId,
            Transform2D transform,
            float opacity = 1f,
            bool enabled = true,
            LayoutMode layoutMode = LayoutMode.Stretch,
            BlendMode blendMode = BlendMode.Normal)
        {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            SourceId = sourceId;
            Transform = transform;
            Opacity = opacity;
            Enabled = enabled;
            LayoutMode = layoutMode;
            BlendMode = blendMode;
        }

        public D3D11SharedTextureFrameHandle Handle { get; }

        public SourceId SourceId { get; }

        public Transform2D Transform { get; }

        public float Opacity { get; }

        public bool Enabled { get; }

        public LayoutMode LayoutMode { get; }

        public BlendMode BlendMode { get; }
    }

    private static void FillSharedTexture(
        D3D11GpuDevice device,
        D3D11SharedTextureFrameHandle handle,
        ColorRgba color)
    {
        handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);

        try
        {
            using var renderTargetView = device.Device.CreateRenderTargetView(handle.Texture);
            device.Context.ClearRenderTargetView(
                renderTargetView,
                new Color4(color.R, color.G, color.B, color.A));
        }
        finally
        {
            handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
            handle.NotifyCaptureReleasedToConsumer();
        }
    }

    private static void AssertPixelNear(
        VulkanReadbackPixel pixel,
        byte expectedR,
        byte expectedG,
        byte expectedB,
        byte expectedA,
        byte tolerance = 1)
    {
        Assert.InRange((int)pixel.R, expectedR - tolerance, expectedR + tolerance);
        Assert.InRange((int)pixel.G, expectedG - tolerance, expectedG + tolerance);
        Assert.InRange((int)pixel.B, expectedB - tolerance, expectedB + tolerance);
        Assert.InRange((int)pixel.A, expectedA - tolerance, expectedA + tolerance);
    }

    private static RenderOutputBindingSnapshot CreateOffscreenBinding(
        RenderOutputId outputId,
        uint width,
        uint height) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            SurfaceSize = new FrameSize(width, height),
            BindingVersion = 1
        };

    private static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = [],
            Outputs = []
        };

    private static bool TryCreateSharedTexture(out D3D11GpuDevice device, out D3D11SharedTextureFrameHandle handle)
    {
        device = null!;
        handle = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateCp1Context(
        out Cp1TestContext? context,
        IVulkanRendererFaultInjector? faultInjector = null)
    {
        context = null;

        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return false;

        if (!TryCreateRenderer(out var renderer, faultInjector))
        {
            sharedHandle.Dispose();
            device.Dispose();
            return false;
        }

        context = new Cp1TestContext(device, sharedHandle, renderer!);
        return true;
    }

    private static async Task ReleaseSubmissionAsync(IRenderFrameSubmission submission)
    {
        await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        submission.DisposeCompleted();
    }

    private static bool TryCreateRenderer(
        out TestRendererContext? context,
        IVulkanRendererFaultInjector? faultInjector = null)
    {
        context = null;

        try
        {
            var guard = new RenderThreadGuard();
            if (!MediaForgeVulkanRenderer.TryCreate(
                guard,
                diagnostics: null,
                faultInjector ?? NullVulkanRendererFaultInjector.Instance,
                out var backend) ||
                backend is null)
            {
                return false;
            }

            context = new TestRendererContext(guard, backend);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TestRendererContext : IDisposable
    {
        public TestRendererContext(RenderThreadGuard guard, MediaForgeVulkanRenderer backend)
        {
            Guard = guard;
            Backend = backend;
        }

        public RenderThreadGuard Guard { get; }

        public MediaForgeVulkanRenderer Backend { get; }

        public void Dispose() => Backend.Dispose();
    }

    private sealed class Cp1TestContext : IDisposable
    {
        private readonly D3D11GpuDevice _device;
        private readonly D3D11SharedTextureFrameHandle _sharedHandle;
        private readonly TestRendererContext _renderer;

        public Cp1TestContext(
            D3D11GpuDevice device,
            D3D11SharedTextureFrameHandle sharedHandle,
            TestRendererContext renderer)
        {
            _device = device;
            _sharedHandle = sharedHandle;
            _renderer = renderer;
        }

        public D3D11SharedTextureFrameHandle SharedHandle => _sharedHandle;

        public D3D11GpuDevice Device => _device;

        public RenderThreadGuard Guard => _renderer.Guard;

        public MediaForgeVulkanRenderer Backend => _renderer.Backend;

        public void Dispose()
        {
            _renderer.Dispose();
            _sharedHandle.Dispose();
            _device.Dispose();
        }
    }
}
