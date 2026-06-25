using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public class VulkanIntermediateTargetPoolTests
{
    [Fact]
    public async Task Nested_canvas_reuses_intermediate_target_between_frames()
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
                VulkanOffscreenRenderTargetLifetime.Reset();

                var outputId = RenderOutputId.New();
                var parentId = CanvasId.New();
                var childId = CanvasId.New();
                var child = VulkanCompositionTestHarness.CreateSolidCanvas(
                    childId,
                    new FrameSize(32, 32),
                    WTK.MediaForge.Core.Color.ColorRgba.From(1, 0, 0, 1));
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

                var first = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(first);

                var poolCountAfterFirst = backend.IntermediateTargetPoolLiveCountForTests;
                var disposeCountAfterFirst = VulkanOffscreenRenderTargetLifetime.DisposeCount;

                var second = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(second);

                Assert.True(poolCountAfterFirst >= 2);
                Assert.Equal(poolCountAfterFirst, backend.IntermediateTargetPoolLiveCountForTests);
                Assert.Equal(disposeCountAfterFirst, VulkanOffscreenRenderTargetLifetime.DisposeCount);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Project_update_invalidates_intermediate_target_cache()
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
                    WTK.MediaForge.Core.Color.ColorRgba.From(1, 0, 0, 1));
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

                var first = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(first);

                var beforeInvalidate = backend.IntermediateTargetPoolLiveCountForTests;
                Assert.True(beforeInvalidate > 0);

                backend.InvalidateIntermediateTargetCacheForTests();
                Assert.Equal(0, backend.IntermediateTargetPoolLiveCountForTests);

                var second = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(second);

                Assert.True(backend.IntermediateTargetPoolLiveCountForTests > 0);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Intermediate_target_pool_releases_targets_on_renderer_dispose()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            context!.Guard.BindToCurrentThread();

            try
            {
                var backend = context.Backend;
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.IntermediateTargetPoolLiveCountForTests > 0);

                var disposeBefore = VulkanOffscreenRenderTargetLifetime.DisposeCount;
                backend.InvalidateIntermediateTargetCacheForTests();

                Assert.Equal(0, backend.IntermediateTargetPoolLiveCountForTests);
                Assert.True(VulkanOffscreenRenderTargetLifetime.DisposeCount > disposeBefore);
            }
            finally
            {
                context.Guard.Clear();
            }
        }
    }
}
