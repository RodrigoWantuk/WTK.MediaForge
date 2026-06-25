using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanWin32PanelPresenterRegistry
{
    private static readonly ConcurrentDictionary<PresenterKey, VulkanWin32PanelPresenter> Presenters = new();

    static VulkanWin32PanelPresenterRegistry()
    {
        PreviewPanelPresenterLifecycle.RegisterRemovePresentersForPanel(RemovePresentersForPanel);
    }

    internal static int RegisteredPresenterCountForTests => Presenters.Count;

    internal static int TotalPendingCommandBuffersForTests =>
        Presenters.Values.Sum(static presenter => presenter.PendingCommandBufferCountForTests);

    public static void Present(
        VulkanOffscreenRenderTarget source,
        nint panelHandle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (panelHandle == 0)
            throw new ArgumentException("Panel handle cannot be zero.", nameof(panelHandle));

        var device = source.DeviceContext;
        if (!device.SupportsWin32Presentation)
        {
            throw new NotSupportedException(
                "Win32 GPU preview presentation requires Vulkan surface and swapchain support.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var key = new PresenterKey(device.Device.Handle, panelHandle);
        var presenter = Presenters.GetOrAdd(key, _ => new VulkanWin32PanelPresenter(device, panelHandle));
        presenter.Present(source, cancellationToken);
    }

    public static void RemovePresentersForPanel(nint panelHandle)
    {
        if (panelHandle == 0)
            return;

        foreach (var key in Presenters.Keys)
        {
            if (key.PanelHandle != panelHandle)
                continue;

            if (Presenters.TryRemove(key, out var presenter))
                presenter.Dispose();
        }
    }

    public static void RemovePresenter(VulkanHeadlessDevice device, nint panelHandle)
    {
        if (panelHandle == 0)
            return;

        if (Presenters.TryRemove(new PresenterKey(device.Device.Handle, panelHandle), out var presenter))
            presenter.Dispose();
    }

    private readonly record struct PresenterKey(nint DeviceHandle, nint PanelHandle);
}

internal sealed unsafe class VulkanWin32PanelPresenter : IDisposable
{
    private const ulong WaitSliceNanoseconds = 50_000_000;
    private static readonly TimeSpan DisposeFenceTimeout = TimeSpan.FromSeconds(5);

    private readonly VulkanHeadlessDevice _device;
    private readonly nint _panelHandle;
    private readonly KhrSurface _khrSurface;
    private readonly KhrWin32Surface _khrWin32Surface;
    private readonly KhrSwapchain _khrSwapchain;
    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = [];
    private ImageLayout[] _swapchainImageLayouts = [];
    private VulkanSemaphore _imageAvailable;
    private VulkanSemaphore _renderFinished;
    private Fence _presentFence;
    private CommandBuffer _pendingCommandBuffer;
    private bool _hasPendingCommandBuffer;
    private int _disposed;

    internal int PendingCommandBufferCountForTests => _hasPendingCommandBuffer ? 1 : 0;

    public VulkanWin32PanelPresenter(VulkanHeadlessDevice device, nint panelHandle)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _panelHandle = panelHandle;
        _khrSurface = device.KhrSurface ?? throw new InvalidOperationException("KHR_surface is unavailable.");
        _khrWin32Surface = device.KhrWin32Surface ?? throw new InvalidOperationException("KHR_win32_surface is unavailable.");
        _khrSwapchain = device.KhrSwapchain ?? throw new InvalidOperationException("KHR_swapchain is unavailable.");

        CreateSurface();
        CreateSwapchain(ChooseExtent(default));
        CreateSyncObjects();
    }

    public void Present(VulkanOffscreenRenderTarget source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var desiredExtent = ChooseExtent(source.Size);
        if (desiredExtent.Width != _swapchainExtent.Width || desiredExtent.Height != _swapchainExtent.Height)
            RecreateSwapchain(desiredExtent, cancellationToken);

        var vk = _device.Vk;
        var device = _device.Device;

        lock (_device.CommandQueueGate)
        {
            WaitForFence(vk, device, _presentFence, cancellationToken);
            ReleasePendingCommandBuffer();

            uint imageIndex = 0;
            var acquireResult = AcquireNextImage(ref imageIndex, cancellationToken);

            if (acquireResult is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
            {
                RecreateSwapchain(desiredExtent, cancellationToken);
                acquireResult = AcquireNextImage(ref imageIndex, cancellationToken);

                if (acquireResult is Result.ErrorOutOfDateKhr)
                    return;
            }

            if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
                throw new InvalidOperationException($"Failed to acquire swapchain image: {acquireResult}");

            var commandBuffer = BeginOneTimeCommandBuffer();
            var destinationImage = _swapchainImages[imageIndex];
            var destinationLayout = _swapchainImageLayouts[imageIndex];
            var originalSourceLayout = source.CurrentLayout;

            if (originalSourceLayout != ImageLayout.TransferSrcOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    source.Image,
                    originalSourceLayout,
                    ImageLayout.TransferSrcOptimal);
            }

            if (destinationLayout != ImageLayout.TransferDstOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    destinationImage,
                    destinationLayout,
                    ImageLayout.TransferDstOptimal);
            }

            var blitRegion = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1
                }
            };
            blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.SrcOffsets[1] = new Offset3D((int)source.Size.Width, (int)source.Size.Height, 1);
            blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.DstOffsets[1] = new Offset3D((int)_swapchainExtent.Width, (int)_swapchainExtent.Height, 1);

            vk.CmdBlitImage(
                commandBuffer,
                source.Image,
                ImageLayout.TransferSrcOptimal,
                destinationImage,
                ImageLayout.TransferDstOptimal,
                1,
                in blitRegion,
                Filter.Linear);

            VulkanImageLayoutTransition.Transition(
                vk,
                commandBuffer,
                destinationImage,
                ImageLayout.TransferDstOptimal,
                ImageLayout.PresentSrcKhr);
            _swapchainImageLayouts[imageIndex] = ImageLayout.PresentSrcKhr;

            if (originalSourceLayout != ImageLayout.TransferSrcOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    source.Image,
                    ImageLayout.TransferSrcOptimal,
                    originalSourceLayout);
            }

            source.CurrentLayout = originalSourceLayout;

            if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
                throw new InvalidOperationException("Failed to end preview present command buffer.");

            vk.ResetFences(device, 1, in _presentFence);

            var waitSemaphores = stackalloc VulkanSemaphore[] { _imageAvailable };
            var waitStages = PipelineStageFlags.TransferBit;
            var signalSemaphores = stackalloc VulkanSemaphore[] { _renderFinished };

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = waitSemaphores,
                PWaitDstStageMask = &waitStages,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = signalSemaphores
            };

            if (vk.QueueSubmit(_device.GraphicsQueue, 1, in submitInfo, _presentFence) != Result.Success)
                throw new InvalidOperationException("Failed to submit preview present command buffer.");

            _pendingCommandBuffer = commandBuffer;
            _hasPendingCommandBuffer = true;

            var swapchain = _swapchain;
            var presentInfo = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = signalSemaphores,
                SwapchainCount = 1,
                PSwapchains = &swapchain,
                PImageIndices = &imageIndex
            };

            var presentResult = _khrSwapchain.QueuePresent(_device.GraphicsQueue, in presentInfo);
            if (presentResult is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
                RecreateSwapchain(desiredExtent, cancellationToken);
            else if (presentResult != Result.Success)
                throw new InvalidOperationException($"Failed to present swapchain image: {presentResult}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var vk = _device.Vk;
        var device = _device.Device;

        lock (_device.CommandQueueGate)
        {
            try
            {
                WaitForFence(vk, device, _presentFence, CancellationToken.None, DisposeFenceTimeout);
            }
            catch (TimeoutException)
            {
            }

            ReleasePendingCommandBuffer();
            DestroySwapchain();

            if (_presentFence.Handle != 0)
                vk.DestroyFence(device, _presentFence, null);

            if (_renderFinished.Handle != 0)
                vk.DestroySemaphore(device, _renderFinished, null);

            if (_imageAvailable.Handle != 0)
                vk.DestroySemaphore(device, _imageAvailable, null);

            if (_surface.Handle != 0)
                _khrSurface.DestroySurface(_device.Instance, _surface, null);
        }
    }

    private Result AcquireNextImage(ref uint acquiredIndex, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = _khrSwapchain.AcquireNextImage(
                _device.Device,
                _swapchain,
                WaitSliceNanoseconds,
                _imageAvailable,
                default,
                ref acquiredIndex);

            if (result != Result.Timeout)
                return result;
        }
    }

    private static void WaitForFence(
        Vk vk,
        Device device,
        Fence fence,
        CancellationToken cancellationToken,
        TimeSpan? totalTimeout = null)
    {
        var deadline = totalTimeout is { } timeout
            ? Environment.TickCount64 + (long)timeout.TotalMilliseconds
            : long.MaxValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Timed out waiting for preview present fence.");

            var result = vk.WaitForFences(device, 1, in fence, true, WaitSliceNanoseconds);
            if (result == Result.Success)
                return;

            if (result != Result.Timeout)
                throw new InvalidOperationException($"Failed to wait for preview present fence: {result}");
        }
    }

    private void ReleasePendingCommandBuffer()
    {
        if (!_hasPendingCommandBuffer)
            return;

        _device.Vk.FreeCommandBuffers(
            _device.Device,
            _device.CommandPool,
            1,
            in _pendingCommandBuffer);

        _pendingCommandBuffer = default;
        _hasPendingCommandBuffer = false;
    }

    private void CreateSurface()
    {
        var createInfo = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hinstance = GetModuleHandle(null),
            Hwnd = _panelHandle
        };

        if (_khrWin32Surface.CreateWin32Surface(_device.Instance, in createInfo, null, out _surface) != Result.Success)
            throw new InvalidOperationException("vkCreateWin32SurfaceKHR failed.");
    }

    private void CreateSyncObjects()
    {
        var vk = _device.Vk;
        var device = _device.Device;

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        if (vk.CreateSemaphore(device, in semaphoreInfo, null, out _imageAvailable) != Result.Success ||
            vk.CreateSemaphore(device, in semaphoreInfo, null, out _renderFinished) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create preview present semaphores.");
        }

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        if (vk.CreateFence(device, in fenceInfo, null, out _presentFence) != Result.Success)
            throw new InvalidOperationException("Failed to create preview present fence.");
    }

    private void CreateSwapchain(Extent2D extent)
    {
        SurfaceCapabilitiesKHR capabilities;
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(
            _device.PhysicalDevice,
            _surface,
            &capabilities);

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(
            _device.PhysicalDevice,
            _surface,
            ref formatCount,
            null);

        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPtr = formats)
        {
            _khrSurface.GetPhysicalDeviceSurfaceFormats(
                _device.PhysicalDevice,
                _surface,
                ref formatCount,
                formatsPtr);
        }

        var surfaceFormat = formats[0];
        foreach (var candidate in formats)
        {
            if (candidate.Format == Format.B8G8R8A8Unorm &&
                candidate.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                surfaceFormat = candidate;
                break;
            }
        }

        _swapchainFormat = surfaceFormat.Format;
        _swapchainExtent = ChooseSwapExtent(capabilities, extent);

        var queueFamilyIndex = _device.GraphicsQueueFamilyIndex;
        var queueFamilyIndices = stackalloc uint[] { queueFamilyIndex };

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = Math.Max(2u, capabilities.MinImageCount),
            ImageFormat = _swapchainFormat,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            QueueFamilyIndexCount = 1,
            PQueueFamilyIndices = queueFamilyIndices,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true
        };

        if (_khrSwapchain.CreateSwapchain(_device.Device, in createInfo, null, out _swapchain) != Result.Success)
            throw new InvalidOperationException("vkCreateSwapchainKHR failed.");

        uint imageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, null);
        _swapchainImages = new Image[imageCount];
        fixed (Image* imagesPtr = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, imagesPtr);
        }

        _swapchainImageLayouts = new ImageLayout[imageCount];
    }

    private void RecreateSwapchain(Extent2D extent, CancellationToken cancellationToken)
    {
        var vk = _device.Vk;
        var device = _device.Device;

        WaitForFence(vk, device, _presentFence, cancellationToken);
        ReleasePendingCommandBuffer();
        vk.DeviceWaitIdle(device);
        DestroySwapchain();
        CreateSwapchain(extent);
    }

    private void DestroySwapchain()
    {
        if (_swapchain.Handle == 0)
            return;

        _khrSwapchain.DestroySwapchain(_device.Device, _swapchain, null);
        _swapchain = default;
        _swapchainImages = [];
        _swapchainImageLayouts = [];
    }

    private Extent2D ChooseExtent(FrameSize frameSize)
    {
        if (GetClientRect(_panelHandle, out var rect))
        {
            var width = Math.Max(1u, (uint)(rect.Right - rect.Left));
            var height = Math.Max(1u, (uint)(rect.Bottom - rect.Top));
            return new Extent2D(width, height);
        }

        if (frameSize.Width > 0 && frameSize.Height > 0)
            return new Extent2D(frameSize.Width, frameSize.Height);

        return new Extent2D(640, 360);
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, Extent2D desired)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return capabilities.CurrentExtent;

        var width = Math.Clamp(desired.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
        var height = Math.Clamp(desired.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);
        return new Extent2D(width, height);
    }

    private CommandBuffer BeginOneTimeCommandBuffer()
    {
        var vk = _device.Vk;
        var device = _device.Device;

        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _device.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (vk.AllocateCommandBuffers(device, in allocateInfo, out var commandBuffer) != Result.Success)
            throw new InvalidOperationException("Failed to allocate preview present command buffer.");

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (vk.BeginCommandBuffer(commandBuffer, in beginInfo) != Result.Success)
            throw new InvalidOperationException("Failed to begin preview present command buffer.");

        return commandBuffer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
