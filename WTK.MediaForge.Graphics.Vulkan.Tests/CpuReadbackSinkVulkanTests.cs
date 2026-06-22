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
public class CpuReadbackSinkVulkanTests
{
    [Fact]
    public async Task CpuReadbackSink_center_pixel_matches_expected_color()
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
                CpuReadbackFrame? readback = null;
                var sink = new CpuReadbackSink(onFrame: (frame, _) =>
                {
                    readback = frame;
                    return ValueTask.CompletedTask;
                });

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
                await sink.StartAsync(VulkanCompositionTestHarness.CreateSinkContext(outputId, size), CancellationToken.None);

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                var outputFrames = submission.AcquireOutputFrames();
                var frame = Assert.Single(outputFrames.Frames);
                await sink.OnFrameAsync(
                    outputFrames.CreateLease(frame, VulkanCompositionTestHarness.CreateOutputFrameInfo(frame, sink.Id)),
                    CancellationToken.None);

                submission.DisposeCompleted();

                Assert.NotNull(readback);
                Assert.Equal(64 * 4, readback.StrideBytes);
                Assert.Equal(RenderPixelFormat.Rgba8Unorm, readback.Format);
                VulkanCompositionTestHarness.AssertPixelNear(VulkanCompositionTestHarness.ReadPixel(readback, 32, 32), 255, 0, 0, 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Sample_offscreen_reads_actual_pixels()
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
                var size = new FrameSize(32, 32);
                CpuReadbackFrame? readback = null;
                var sink = new CpuReadbackSink(onFrame: (frame, _) =>
                {
                    readback = frame;
                    return ValueTask.CompletedTask;
                });

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
                await sink.StartAsync(VulkanCompositionTestHarness.CreateSinkContext(outputId, size), CancellationToken.None);

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(32, 32) },
                    outputLetterboxColor: ColorRgba.Transparent);

                var submission = backend.Submit(snapshot);
                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                var outputFrames = submission.AcquireOutputFrames();
                var frame = Assert.Single(outputFrames.Frames);
                await sink.OnFrameAsync(
                    outputFrames.CreateLease(frame, VulkanCompositionTestHarness.CreateOutputFrameInfo(frame, sink.Id, frameNumber: 12)),
                    CancellationToken.None);

                submission.DisposeCompleted();

                Assert.NotNull(readback);
                Assert.Equal(12, readback.FrameNumber);
                Assert.Equal(size, readback.Size);
                Assert.Equal(32 * 32 * 4, readback.Pixels.Length);
                VulkanCompositionTestHarness.AssertPixelNear(VulkanCompositionTestHarness.ReadPixel(readback, 16, 16), 0, 255, 0, 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task CpuReadbackSink_frame_pixels_are_not_overwritten_by_next_submit()
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
                var size = new FrameSize(32, 32);
                CpuReadbackFrame? firstReadback = null;
                var sink = new CpuReadbackSink(onFrame: (frame, _) =>
                {
                    firstReadback = frame;
                    return ValueTask.CompletedTask;
                });

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
                await sink.StartAsync(VulkanCompositionTestHarness.CreateSinkContext(outputId, size), CancellationToken.None);

                VulkanCompositionTestHarness.FillSharedTexture(context.Device, context.SharedHandle, ColorRgba.From(1, 0, 0, 1));
                using var firstSnapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(32, 32) },
                    outputLetterboxColor: ColorRgba.Transparent);
                var firstSubmission = backend.Submit(firstSnapshot);
                await firstSubmission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                var firstOutputFrames = firstSubmission.AcquireOutputFrames();
                var firstFrame = Assert.Single(firstOutputFrames.Frames);
                var firstLease = firstOutputFrames.CreateLease(
                    firstFrame,
                    VulkanCompositionTestHarness.CreateOutputFrameInfo(firstFrame, sink.Id, frameNumber: 1));

                VulkanCompositionTestHarness.FillSharedTexture(context.Device, context.SharedHandle, ColorRgba.From(0, 0, 1, 1));
                using var secondSnapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(32, 32) },
                    outputLetterboxColor: ColorRgba.Transparent);
                var secondSubmission = backend.Submit(secondSnapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(secondSubmission);

                await sink.OnFrameAsync(firstLease, CancellationToken.None);
                firstSubmission.DisposeCompleted();

                Assert.NotNull(firstReadback);
                VulkanCompositionTestHarness.AssertPixelNear(VulkanCompositionTestHarness.ReadPixel(firstReadback, 16, 16), 255, 0, 0, 255);
                Assert.True(backend.TryReadOffscreenPixel(outputId, 16, 16, out var latestPixel));
                VulkanCompositionTestHarness.AssertPixelNear(latestPixel, 0, 0, 255, 255);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
