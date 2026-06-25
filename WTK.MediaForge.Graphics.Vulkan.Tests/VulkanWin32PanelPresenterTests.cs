using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class VulkanWin32PanelPresenterTests
{
    [Fact]
    public void Preview_attach_detach_repeated_does_not_leak_presenters()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create();
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
                Assert.Equal(1, VulkanWin32PanelPresenterRegistry.RegisteredPresenterCountForTests);

                PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
                Assert.Equal(0, VulkanWin32PanelPresenterRegistry.RegisteredPresenterCountForTests);
            }
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_present_releases_previous_command_buffer_after_fence()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create();
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
            Assert.Equal(1, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);

            VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
            Assert.Equal(1, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_present_repeated_frames_does_not_leak_command_buffers()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create();
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            for (var frame = 0; frame < 8; frame++)
                VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);

            Assert.Equal(1, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_dispose_releases_pending_command_buffer()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create();
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
            Assert.Equal(1, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);

            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Assert.Equal(0, VulkanWin32PanelPresenterRegistry.RegisteredPresenterCountForTests);
            Assert.Equal(0, VulkanWin32PanelPresenterRegistry.TotalPendingCommandBuffersForTests);
        }
        finally
        {
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_present_recovers_from_panel_resize()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create(width: 640, height: 360);
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
            Assert.True(VulkanWin32PanelPresenterRegistry.TryGetSwapchainExtentForTests(device, panelHandle, out var initialExtent));

            var initialClient = Win32TestPanel.GetClientSize(panelHandle);
            Assert.Equal((int)initialExtent.Width, initialClient.Width);
            Assert.Equal((int)initialExtent.Height, initialClient.Height);

            Win32TestPanel.ResizeClient(panelHandle, 480, 240);
            PreviewPanelClientSizeTracker.NotifyClientSize(panelHandle, 480, 240);
            VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);

            Assert.True(VulkanWin32PanelPresenterRegistry.TryGetSwapchainExtentForTests(device, panelHandle, out var resizedExtent));
            var resizedClient = Win32TestPanel.GetClientSize(panelHandle);
            Assert.Equal(480, resizedClient.Width);
            Assert.Equal(240, resizedClient.Height);
            Assert.Equal((uint)resizedClient.Width, resizedExtent.Width);
            Assert.Equal((uint)resizedClient.Height, resizedExtent.Height);

            Win32TestPanel.ResizeClient(panelHandle, 320, 180);
            PreviewPanelClientSizeTracker.NotifyClientSize(panelHandle, 320, 180);
            for (var frame = 0; frame < 4; frame++)
                VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);

            Assert.True(VulkanWin32PanelPresenterRegistry.TryGetSwapchainExtentForTests(device, panelHandle, out var shrunkExtent));
            Assert.Equal(320u, shrunkExtent.Width);
            Assert.Equal(180u, shrunkExtent.Height);

            Win32TestPanel.ResizeClient(panelHandle, 640, 360);
            PreviewPanelClientSizeTracker.NotifyClientSize(panelHandle, 640, 360);
            for (var frame = 0; frame < 4; frame++)
                VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);

            Assert.True(VulkanWin32PanelPresenterRegistry.TryGetSwapchainExtentForTests(device, panelHandle, out var restoredExtent));
            Assert.Equal(640u, restoredExtent.Width);
            Assert.Equal(360u, restoredExtent.Height);
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_present_does_not_block_when_swapchain_is_repeatedly_out_of_date()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create(width: 640, height: 360);
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

            for (var resize = 0; resize < 6; resize++)
            {
                var width = 640 - resize * 40;
                var height = 360 - resize * 20;
                Win32TestPanel.ResizeClient(panelHandle, width, height);
                PreviewPanelClientSizeTracker.NotifyClientSize(panelHandle, (uint)width, (uint)height);

                for (var frame = 0; frame < 2; frame++)
                    VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, CancellationToken.None);
            }

            Assert.True(VulkanWin32PanelPresenterRegistry.TryGetSwapchainExtentForTests(device, panelHandle, out var extent));
            Assert.Equal(440u, extent.Width);
            Assert.Equal(260u, extent.Height);
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }

    [Fact]
    public void Preview_present_honors_cancellation_during_acquire()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();
        if (!device.SupportsWin32Presentation)
            return;

        var panelHandle = Win32TestPanel.Create();
        try
        {
            using var target = new VulkanOffscreenRenderTarget(device, new FrameSize(64, 64));
            target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() =>
                VulkanWin32PanelPresenterRegistry.Present(target, panelHandle, cts.Token));
        }
        finally
        {
            PreviewPanelPresenterLifecycle.RemovePresentersForPanel(panelHandle);
            Win32TestPanel.Destroy(panelHandle);
        }
    }
}
