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
