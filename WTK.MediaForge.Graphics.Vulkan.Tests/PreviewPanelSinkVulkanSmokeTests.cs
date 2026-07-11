using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class PreviewPanelSinkVulkanSmokeTests
{
    [Fact]
    public async Task PreviewPanelSink_presents_renderer_output_to_real_win32_panel_and_releases_lease()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            if (!context!.Backend.SupportsWin32PresentationForTests)
                return;

            var panelHandle = Win32TestPanel.Create(width: 640, height: 360);
            var outputId = RenderOutputId.New();
            var canvasId = CanvasId.New();
            var size = new FrameSize(64, 64);
            var sink = new PreviewPanelSink(panelHandle);
            IRenderFrameSubmission? submission = null;
            var submissionDisposed = false;

            try
            {
                context.Guard.BindToCurrentThread();
                VulkanCompositionTestHarness.FillSharedTexture(
                    context.Device,
                    context.SharedHandle,
                    ColorRgba.From(1, 0, 0, 1));

                context.Backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));
                await sink.StartAsync(
                    VulkanCompositionTestHarness.CreateSinkContext(outputId, size),
                    CancellationToken.None);

                using var snapshot = VulkanCompositionTestHarness.CreateCp1Snapshot(
                    context.SharedHandle,
                    canvasId,
                    outputId,
                    canvasSize: size,
                    outputSize: size,
                    transform: new Transform2D { Size = new CanvasSize(64, 64) },
                    outputLetterboxColor: ColorRgba.Transparent);

                submission = context.Backend.Submit(snapshot);
                await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                var outputFrames = submission.AcquireOutputFrames();
                var frame = Assert.Single(outputFrames.Frames);
                var lease = outputFrames.CreateLease(
                    frame,
                    VulkanCompositionTestHarness.CreateOutputFrameInfo(frame, sink.Id));

                context.Guard.Clear();
                await sink.OnFrameAsync(lease, CancellationToken.None);

                Assert.False(outputFrames.HasOutstandingLeases);
                Assert.Equal(1, VulkanWin32PanelPresenterRegistry.RegisteredPresenterCountForTests);
                Assert.Equal(1, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);

                await sink.StopAsync(CancellationToken.None);

                Assert.Equal(0, VulkanWin32PanelPresenterRegistry.RegisteredPresenterCountForTests);
                Assert.Equal(0, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);

                submission.DisposeCompleted();
                submissionDisposed = true;
            }
            finally
            {
                context.Guard.Clear();

                try
                {
                    await sink.DisposeAsync();
                }
                catch (ObjectDisposedException)
                {
                }

                if (submission is not null &&
                    !submissionDisposed &&
                    submission.IsCompleted &&
                    !submission.HasOutstandingOutputFrameLeases)
                {
                    submission.DisposeCompleted();
                }

                PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
                Win32TestPanel.Destroy(panelHandle);
            }
        }
    }
}
