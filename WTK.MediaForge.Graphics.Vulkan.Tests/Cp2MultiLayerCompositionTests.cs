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
public class Cp2MultiLayerCompositionTests
{
    [Fact]
    public async Task Cp2_same_source_two_layers_render_at_different_positions()
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
                var sourceId = SourceId.New();
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
                            sourceId,
                            new Transform2D
                            {
                                Position = new CanvasPoint(0, 0),
                                Size = new CanvasSize(16, 16)
                            }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            sourceId,
                            new Transform2D
                            {
                                Position = new CanvasPoint(32, 32),
                                Size = new CanvasSize(16, 16)
                            })
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 8, 8, out var first));
                VulkanCompositionTestHarness.AssertPixelNear(first, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 40, 40, out var second));
                VulkanCompositionTestHarness.AssertPixelNear(second, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 24, 24, out var gap));
                VulkanCompositionTestHarness.AssertPixelNear(gap, expectedR: 0, expectedG: 0, expectedB: 0, expectedA: 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp2_two_sources_render_expected_pixels()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
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
                    ]);

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
    public async Task Cp2_top_layer_overwrites_bottom_when_alpha_1()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    VulkanCompositionTestHarness.CreateFullFrameLayers(context.SharedHandle, blueHandle));

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
    public async Task Cp2_top_layer_alpha_blends_over_bottom()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            blueHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            opacity: 0.5f)
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
    public async Task Cp2_layer_order_matches_canvas_object_order()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            blueHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) })
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
    public async Task Cp2_layer_transform_positions_pixels_correctly()
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D
                            {
                                Position = new CanvasPoint(24, 8),
                                Size = new CanvasSize(16, 16)
                            })
                    ]);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 28, 12, out var inside));
                VulkanCompositionTestHarness.AssertPixelNear(inside, expectedR: 255, expectedG: 0, expectedB: 0, expectedA: 255);
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
    public async Task Cp2_disabled_layer_is_not_rendered()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            blueHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            enabled: false)
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
    public async Task Cp2_opacity_zero_layer_is_transparent()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        using (var blueHandle = VulkanCompositionTestHarness.CreateFilledSharedTexture(context!.Device, ColorRgba.From(0, 0, 1, 1)))
        {
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

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    [
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            context.SharedHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) }),
                        new VulkanCompositionTestHarness.Cp2LayerSpec(
                            blueHandle,
                            SourceId.New(),
                            new Transform2D { Size = new CanvasSize(64, 64) },
                            opacity: 0f)
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
}
