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
public class VulkanSubmissionLifetimeTests
{
    [Fact]
    public async Task Offscreen_target_survives_unbind_until_submission_fence_completes()
    {
        if (!VulkanCompositionTestHarness.TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        VulkanOffscreenRenderTargetLifetime.Reset();

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
                    SurfaceSize = new FrameSize(640, 480),
                    BindingVersion = 1
                });

                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);

                var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);

                backend.UnbindOutput(outputId);

                Assert.Equal(0, backend.OffscreenTargetCount);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(0, VulkanOffscreenRenderTargetLifetime.DisposeCount);

                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.Equal(0, backend.TextureRegistryActiveLeaseCount);
                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.DisposeCount);
                Assert.Equal(1, backend.IntermediateTargetPoolLiveCountForTests);

                snapshot.Dispose();
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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                Assert.Equal(0, VulkanSubmissionResourceLifetime.FreedDescriptorSets);

                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                for (var i = 0; i < 50; i++)
                {
                    using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                    var submission = backend.Submit(snapshot);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);
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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 128, 128));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: new FrameSize(128, 128),
                    outputSize: new FrameSize(128, 128),
                    transform: new Transform2D { Size = new CanvasSize(128, 128) },
                    outputLetterboxColor: ColorRgba.Transparent,
                    sourceLayerCount: 40);

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Assert.Equal(2, VulkanSubmissionResourceLifetime.LiveDescriptorSets);

                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using (var firstSnapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId))
                {
                    var first = backend.Submit(firstSnapshot);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(first);
                }

                using (var lease = backend.TextureRegistry.Acquire(context.SharedHandle))
                    Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);

                using (var secondSnapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId))
                {
                    var second = backend.Submit(secondSnapshot);
                    await VulkanCompositionTestHarness.ReleaseSubmissionAsync(second);
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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

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

        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context, faultInjector))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));
                Assert.True(backend.TryGetOffscreenTargetLayout(outputId, out var initialLayout));
                Assert.Equal(ImageLayout.Undefined, initialLayout);

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);

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
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
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

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, 1280, 720));

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(context.SharedHandle, canvasId, outputId);
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
}
