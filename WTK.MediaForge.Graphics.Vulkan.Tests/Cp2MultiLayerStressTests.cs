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
[Trait("Category", "Performance")]
[Collection("VulkanComposition")]
public class Cp2MultiLayerStressTests
{
    [Fact]
    public async Task Cp2_repeated_multi_layer_submits_do_not_exhaust_descriptor_pool()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA"), "1", StringComparison.Ordinal),
                "Vulkan performance workload was required but no compatible Vulkan/D3D11 interop context was available.");
            return;
        }

        VulkanSubmissionResourceLifetime.Reset();

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

                for (var i = 0; i < 50; i++)
                {
                    using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                        canvasId,
                        outputId,
                        size,
                        size,
                        VulkanCompositionTestHarness.CreateFullFrameLayers(context.SharedHandle, blueHandle));

                    var submission = backend.Submit(snapshot);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);
                }

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(100, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
                Assert.Equal(150, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
    [Fact]
    public async Task Cp2_multi_layer_submission_dispose_releases_framebuffers_descriptors_and_surfaces()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA"), "1", StringComparison.Ordinal),
                "Vulkan performance workload was required but no compatible Vulkan/D3D11 interop context was available.");
            return;
        }

        VulkanSubmissionResourceLifetime.Reset();
        VulkanOffscreenRenderTargetLifetime.Reset();

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
                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);

                using var snapshot = VulkanCompositionTestHarness.CreateCp2Snapshot(
                    canvasId,
                    outputId,
                    size,
                    size,
                    VulkanCompositionTestHarness.CreateFullFrameLayers(context.SharedHandle, blueHandle));

                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(3, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);

                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
                Assert.Equal(3, VulkanSubmissionResourceLifetime.FreedDescriptorSets);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(1, backend.IntermediateTargetPoolLiveCountForTests);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
