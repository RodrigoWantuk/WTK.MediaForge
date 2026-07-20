using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanWin32PanelPresenterRegistry
{
    private static readonly ConcurrentDictionary<PresenterKey, VulkanWin32PanelPresenter> Presenters = new();

    static VulkanWin32PanelPresenterRegistry()
    {
        PreviewPanelPresenterLifecycle.RegisterRemovePresentersForPanel(RemovePresentersForPanelAsync);
    }

    internal static int RegisteredPresenterCountForTests => Presenters.Count;

    internal static int TotalPendingCommandBuffersForTests =>
        Presenters.Values.Sum(static presenter => presenter.PendingCommandBufferCountForTests);

    internal static bool TryGetSwapchainExtentForTests(
        VulkanHeadlessDevice device,
        nint panelHandle,
        out Extent2D extent)
    {
        extent = default;

        if (panelHandle == 0)
            return false;

        if (!Presenters.TryGetValue(new PresenterKey(device.Device.Handle, panelHandle), out var presenter))
            return false;

        extent = presenter.SwapchainExtentForTests;
        return true;
    }

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

    public static async ValueTask RemovePresentersForPanelAsync(
        nint panelHandle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (panelHandle == 0)
            return;

        foreach (var key in Presenters.Keys)
        {
            if (key.PanelHandle != panelHandle)
                continue;

            if (!Presenters.TryGetValue(key, out var presenter))
                continue;

            await presenter.DisposeAsync(timeout, cancellationToken).ConfigureAwait(false);
            Presenters.TryRemove(key, out _);
        }
    }

    public static async ValueTask RemovePresenterAsync(
        VulkanHeadlessDevice device,
        nint panelHandle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (panelHandle == 0)
            return;

        var key = new PresenterKey(device.Device.Handle, panelHandle);
        if (!Presenters.TryGetValue(key, out var presenter))
            return;

        await presenter.DisposeAsync(timeout, cancellationToken).ConfigureAwait(false);
        Presenters.TryRemove(key, out _);
    }

    private readonly record struct PresenterKey(nint DeviceHandle, nint PanelHandle);
}

internal enum VulkanPanelPresenterState
{
    Active,
    Stopping,
    AwaitingGpu,
    Disposed,
    Failed
}

internal interface IVulkanPresenterFenceWaiter
{
    void Wait(Vk vk, Device device, Fence fence, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class VulkanPresenterFenceWaiter : IVulkanPresenterFenceWaiter
{
    public static VulkanPresenterFenceWaiter Instance { get; } = new();

    private VulkanPresenterFenceWaiter()
    {
    }

    public void Wait(Vk vk, Device device, Fence fence, TimeSpan timeout, CancellationToken cancellationToken) =>
        VulkanWin32PanelPresenter.WaitForFence(vk, device, fence, cancellationToken, timeout);
}

internal static class VulkanPanelPresenterAsyncLifecycle
{
    public static async ValueTask DisposeAsync(
        VulkanWin32PanelPresenter presenter,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await presenter.DisposeGateForLifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (presenter.StateForTests == VulkanPanelPresenterState.Disposed)
                return;

            presenter.SetStateForLifecycle(VulkanPanelPresenterState.Stopping);

            try
            {
                await Task.Run(
                        () => presenter.DisposeGpuResourcesForLifecycle(timeout, cancellationToken),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                presenter.SetStateForLifecycle(VulkanPanelPresenterState.Disposed);
            }
            catch (TimeoutException)
            {
                presenter.SetStateForLifecycle(VulkanPanelPresenterState.AwaitingGpu);
                throw;
            }
            catch (OperationCanceledException)
            {
                presenter.SetStateForLifecycle(VulkanPanelPresenterState.AwaitingGpu);
                throw;
            }
            catch
            {
                presenter.SetStateForLifecycle(VulkanPanelPresenterState.Failed);
                throw;
            }
        }
        finally
        {
            presenter.DisposeGateForLifecycle.Release();
        }
    }
}

internal sealed unsafe class VulkanWin32PanelPresenter : IAsyncDisposable
{
    private const int MaxPresentAttempts = 8;
    private const ulong WaitSliceNanoseconds = 50_000_000;
    private static readonly TimeSpan DisposeFenceTimeout = TimeSpan.FromSeconds(5);

    private readonly object _presentLock = new();
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private readonly VulkanHeadlessDevice _device;
    private readonly nint _panelHandle;
    private readonly KhrSurface _khrSurface;
    private readonly KhrWin32Surface _khrWin32Surface;
    private readonly KhrSwapchain _khrSwapchain;
    private readonly IVulkanPresenterFenceWaiter _disposeFenceWaiter;
    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = [];
    private ImageLayout[] _swapchainImageLayouts = [];
    private CommandPool _presentCommandPool;
    private VulkanSemaphore _imageAvailable;
    private VulkanSemaphore _renderFinished;
    private Fence _presentFence;
    private CommandBuffer _pendingCommandBuffer;
    private bool _hasPendingCommandBuffer;
    private int _state = (int)VulkanPanelPresenterState.Active;

    internal int PendingCommandBufferCountForTests => _hasPendingCommandBuffer ? 1 : 0;

    internal Extent2D SwapchainExtentForTests => _swapchainExtent;

    internal VulkanPanelPresenterState StateForTests =>
        (VulkanPanelPresenterState)Volatile.Read(ref _state);

    internal SemaphoreSlim DisposeGateForLifecycle => _disposeGate;

    internal bool ResourcesDestroyedForTests =>
        _surface.Handle == 0 &&
        _swapchain.Handle == 0 &&
        _presentCommandPool.Handle == 0 &&
        _presentFence.Handle == 0 &&
        _imageAvailable.Handle == 0 &&
        _renderFinished.Handle == 0;

    public VulkanWin32PanelPresenter(VulkanHeadlessDevice device, nint panelHandle)
        : this(device, panelHandle, VulkanPresenterFenceWaiter.Instance)
    {
    }

    internal VulkanWin32PanelPresenter(
        VulkanHeadlessDevice device,
        nint panelHandle,
        IVulkanPresenterFenceWaiter disposeFenceWaiter)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _panelHandle = panelHandle;
        _khrSurface = device.KhrSurface ?? throw new InvalidOperationException("KHR_surface is unavailable.");
        _khrWin32Surface = device.KhrWin32Surface ?? throw new InvalidOperationException("KHR_win32_surface is unavailable.");
        _khrSwapchain = device.KhrSwapchain ?? throw new InvalidOperationException("KHR_swapchain is unavailable.");
        _disposeFenceWaiter = disposeFenceWaiter ?? throw new ArgumentNullException(nameof(disposeFenceWaiter));

        CreateSurface();
        CreatePresentCommandPool();
        CreateSwapchain(default);
        CreateSyncObjects();
    }

    public void Present(VulkanOffscreenRenderTarget source, CancellationToken cancellationToken)
    {
        ThrowIfNotActive();
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxPresentAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryPreparePresentFrame(source, cancellationToken, out var commandBuffer, out var imageIndex))
            {
                ForceRecreateSwapchain(source.Size, cancellationToken);
                continue;
            }

            var presentResult = SubmitAndPresent(commandBuffer, imageIndex, cancellationToken);
            if (presentResult is Result.ErrorOutOfDateKhr)
            {
                ForceRecreateSwapchain(source.Size, cancellationToken);
                continue;
            }

            if (presentResult is Result.SuboptimalKhr)
                TryRecreateSwapchainIfNeeded(source.Size, cancellationToken);

            if (presentResult is Result.Success or Result.SuboptimalKhr)
                return;

            throw new InvalidOperationException($"Failed to present swapchain image: {presentResult}");
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(DisposeFenceTimeout, CancellationToken.None);

    internal ValueTask DisposeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Presenter dispose timeout must be positive.");

        return VulkanPanelPresenterAsyncLifecycle.DisposeAsync(this, timeout, cancellationToken);
    }

    internal void SetStateForLifecycle(VulkanPanelPresenterState state) =>
        Volatile.Write(ref _state, (int)state);

    internal void DisposeGpuResourcesForLifecycle(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var vk = _device.Vk;
        var device = _device.Device;

        lock (_presentLock)
        {
            if (_presentFence.Handle != 0)
                _disposeFenceWaiter.Wait(vk, device, _presentFence, timeout, cancellationToken);

            ReleasePendingCommandBuffer();
            DestroySwapchain();

            if (_presentFence.Handle != 0)
            {
                vk.DestroyFence(device, _presentFence, null);
                _presentFence = default;
            }

            if (_renderFinished.Handle != 0)
            {
                vk.DestroySemaphore(device, _renderFinished, null);
                _renderFinished = default;
            }

            if (_imageAvailable.Handle != 0)
            {
                vk.DestroySemaphore(device, _imageAvailable, null);
                _imageAvailable = default;
            }

            if (_presentCommandPool.Handle != 0)
            {
                vk.DestroyCommandPool(device, _presentCommandPool, null);
                _presentCommandPool = default;
            }

            if (_surface.Handle != 0)
            {
                _khrSurface.DestroySurface(_device.Instance, _surface, null);
                _surface = default;
            }
        }
    }

    private void ThrowIfNotActive()
    {
        if (StateForTests != VulkanPanelPresenterState.Active)
            throw new ObjectDisposedException(nameof(VulkanWin32PanelPresenter));
    }

    private Result SubmitAndPresent(CommandBuffer commandBuffer, uint imageIndex, CancellationToken cancellationToken)
    {
        var vk = _device.Vk;
        var device = _device.Device;

        lock (_device.CommandQueueGate)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            return _khrSwapchain.QueuePresent(_device.GraphicsQueue, in presentInfo);
        }
    }

    private bool TryPreparePresentFrame(
        VulkanOffscreenRenderTarget source,
        CancellationToken cancellationToken,
        out CommandBuffer commandBuffer,
        out uint imageIndex)
    {
        commandBuffer = default;
        imageIndex = 0;

        lock (_presentLock)
        {
            if (NeedsSwapchainRecreate(source.Size))
                RecreateSwapchain(source.Size, cancellationToken);

            var vk = _device.Vk;
            var device = _device.Device;

            WaitForFence(vk, device, _presentFence, cancellationToken);
            ReleasePendingCommandBuffer();

            imageIndex = 0;
            var acquireResult = AcquireNextImage(ref imageIndex, cancellationToken);
            if (acquireResult is Result.ErrorOutOfDateKhr)
                return false;

            if (acquireResult is Result.SuboptimalKhr && NeedsSwapchainRecreate(source.Size))
                return false;

            if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
                throw new InvalidOperationException($"Failed to acquire swapchain image: {acquireResult}");

            var recordedCommandBuffer = BeginOneTimeCommandBuffer();
            RecordBlit(source, recordedCommandBuffer, _swapchainImages[imageIndex], _swapchainImageLayouts[imageIndex], imageIndex);

            if (vk.EndCommandBuffer(recordedCommandBuffer) != Result.Success)
                throw new InvalidOperationException("Failed to end preview present command buffer.");

            vk.ResetFences(device, 1, in _presentFence);

            commandBuffer = recordedCommandBuffer;
            return true;
        }
    }

    private void RecordBlit(
        VulkanOffscreenRenderTarget source,
        CommandBuffer commandBuffer,
        Image destinationImage,
        ImageLayout destinationLayout,
        uint imageIndex)
    {
        var vk = _device.Vk;
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

        var clearColor = new ClearColorValue(0f, 0f, 0f, 1f);
        var clearRange = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        vk.CmdClearColorImage(
            commandBuffer,
            destinationImage,
            ImageLayout.TransferDstOptimal,
            &clearColor,
            1,
            &clearRange);

        var fitRect = ContentFitLayout.ComputeFitRect(
            source.Size.Width,
            source.Size.Height,
            _swapchainExtent.Width,
            _swapchainExtent.Height);

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
        blitRegion.DstOffsets[0] = new Offset3D(fitRect.X, fitRect.Y, 0);
        blitRegion.DstOffsets[1] = new Offset3D(fitRect.X + fitRect.Width, fitRect.Y + fitRect.Height, 1);

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

    internal static void WaitForFence(
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
            _presentCommandPool,
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

    private void CreatePresentCommandPool()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = _device.GraphicsQueueFamilyIndex
        };

        if (_device.Vk.CreateCommandPool(_device.Device, in poolInfo, null, out _presentCommandPool) != Result.Success)
            throw new InvalidOperationException("Failed to create preview present command pool.");
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

    private void TryRecreateSwapchainIfNeeded(FrameSize frameSize, CancellationToken cancellationToken)
    {
        if (!NeedsSwapchainRecreate(frameSize))
            return;

        ForceRecreateSwapchain(frameSize, cancellationToken);
    }

    private void ForceRecreateSwapchain(FrameSize frameSize, CancellationToken cancellationToken)
    {
        lock (_presentLock)
        {
            if (_swapchain.Handle == 0)
            {
                CreateSwapchain(frameSize);
                return;
            }

            RecreateSwapchain(frameSize, cancellationToken);
        }
    }

    private bool NeedsSwapchainRecreate(FrameSize frameSize)
    {
        var desired = ChoosePanelClientExtent(frameSize);
        if (desired.Width != _swapchainExtent.Width || desired.Height != _swapchainExtent.Height)
            return true;

        var resolved = QueryResolvedSwapchainExtent(frameSize);
        return resolved.Width != _swapchainExtent.Width || resolved.Height != _swapchainExtent.Height;
    }

    private void CreateSwapchain(FrameSize frameSize, SwapchainKHR oldSwapchain = default)
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
        _swapchainExtent = ResolveSwapchainExtent(capabilities, frameSize);

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
            CompositeAlpha = ChooseCompositeAlpha(capabilities),
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
            OldSwapchain = oldSwapchain
        };

        if (_khrSwapchain.CreateSwapchain(_device.Device, in createInfo, null, out _swapchain) != Result.Success)
            throw new InvalidOperationException("vkCreateSwapchainKHR failed.");

        if (oldSwapchain.Handle != 0)
            _khrSwapchain.DestroySwapchain(_device.Device, oldSwapchain, null);

        uint imageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, null);
        _swapchainImages = new Image[imageCount];
        fixed (Image* imagesPtr = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, imagesPtr);
        }

        _swapchainImageLayouts = new ImageLayout[imageCount];
    }

    private static CompositeAlphaFlagsKHR ChooseCompositeAlpha(SurfaceCapabilitiesKHR capabilities)
    {
        if ((capabilities.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0)
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;

        if ((capabilities.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.InheritBitKhr) != 0)
            return CompositeAlphaFlagsKHR.InheritBitKhr;

        return CompositeAlphaFlagsKHR.InheritBitKhr;
    }

    private void RecreateSwapchain(FrameSize frameSize, CancellationToken cancellationToken)
    {
        var vk = _device.Vk;
        var device = _device.Device;

        WaitForFence(vk, device, _presentFence, cancellationToken);
        ReleasePendingCommandBuffer();

        var oldSwapchain = _swapchain;
        _swapchain = default;
        _swapchainImages = [];
        _swapchainImageLayouts = [];

        CreateSwapchain(frameSize, oldSwapchain);
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

    private Extent2D QueryResolvedSwapchainExtent(FrameSize frameSize)
    {
        SurfaceCapabilitiesKHR capabilities;
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(
            _device.PhysicalDevice,
            _surface,
            &capabilities);

        return ResolveSwapchainExtent(capabilities, frameSize);
    }

    private Extent2D ChoosePanelClientExtent(FrameSize frameSize)
    {
        if (PreviewPanelClientSizeTracker.TryGetClientSize(_panelHandle, out var trackedWidth, out var trackedHeight))
            return new Extent2D(trackedWidth, trackedHeight);

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

    private Extent2D ResolveSwapchainExtent(SurfaceCapabilitiesKHR capabilities, FrameSize frameSize)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue &&
            capabilities.CurrentExtent.Height != uint.MaxValue &&
            capabilities.CurrentExtent.Width > 0 &&
            capabilities.CurrentExtent.Height > 0)
        {
            return capabilities.CurrentExtent;
        }

        var desired = ChoosePanelClientExtent(frameSize);
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
            CommandPool = _presentCommandPool,
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
