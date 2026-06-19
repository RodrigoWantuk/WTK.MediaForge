using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed unsafe class VulkanPreviewRenderer : IDisposable
{
    private readonly Vk _vk;

    private Instance _instance;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;

    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private uint _graphicsQueueFamilyIndex;
    private uint _presentQueueFamilyIndex;

    private KhrSurface? _khrSurface;
    private KhrWin32Surface? _khrWin32Surface;
    private KhrSwapchain? _khrSwapchain;

    private SwapchainKHR _swapchain;
    private Format _swapchainImageFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = Array.Empty<Image>();
    private bool[] _swapchainImageInitialized = Array.Empty<bool>();

    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;

    private Semaphore _imageAvailableSemaphore;
    private Semaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;

    private bool _initialized;
    private bool _disposed;

    private Image _sourceImage;
    private DeviceMemory _sourceMemory;
    private nint _sourceSharedHandle;
    private uint _sourceWidth;
    private uint _sourceHeight;
    private bool _sourceImageInGeneralLayout;

    public VulkanPreviewRenderer()
    {
        _vk = Vk.GetApi();
    }

    public string Initialize(IntPtr hwnd, int width, int height)
    {
        ThrowIfDisposed();

        if (_initialized)
            return "Vulkan renderer already initialized.";

        CreateInstance();
        CreateSurface(hwnd);
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain(width, height);
        CreateCommandPool();
        CreateCommandBuffer();
        CreateSyncObjects();

        _initialized = true;

        return GetSelectedGpuDescription();
    }

    public void DrawFrame()
    {
        ThrowIfDisposed();

        if (!_initialized)
            return;

        if (_swapchain.Handle == 0)
            return;

        Fence* fence = stackalloc Fence[] { _inFlightFence };

        _vk.WaitForFences(_device, 1, fence, true, ulong.MaxValue);
        _vk.ResetFences(_device, 1, fence);

        uint imageIndex = 0;

        var acquireResult = _khrSwapchain!.AcquireNextImage(
            _device,
            _swapchain,
            ulong.MaxValue,
            _imageAvailableSemaphore,
            default,
            &imageIndex);

        if (acquireResult == Result.ErrorOutOfDateKhr)
            return;

        if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
            throw new InvalidOperationException($"AcquireNextImage failed: {acquireResult}");

        RecordCopyOrClearCommandBuffer(imageIndex);

        Semaphore* waitSemaphores = stackalloc Semaphore[] { _imageAvailableSemaphore };
        Semaphore* signalSemaphores = stackalloc Semaphore[] { _renderFinishedSemaphore };
        CommandBuffer* commandBuffers = stackalloc CommandBuffer[] { _commandBuffer };

        PipelineStageFlags* waitStages = stackalloc PipelineStageFlags[]
        {
    PipelineStageFlags.TransferBit
};

        void* submitPNext = null;

        DeviceMemory* acquireSyncs = stackalloc DeviceMemory[] { _sourceMemory };
        DeviceMemory* releaseSyncs = stackalloc DeviceMemory[] { _sourceMemory };

        ulong* acquireKeys = stackalloc ulong[] { 1 };
        ulong* releaseKeys = stackalloc ulong[] { 0 };

        uint* acquireTimeouts = stackalloc uint[] { 1_000_000_000 }; // 1s em nanos

        Win32KeyedMutexAcquireReleaseInfoKHR keyedMutexInfo = default;

        if (_sourceMemory.Handle != 0)
        {
            keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
            {
                SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                AcquireCount = 1,
                PAcquireSyncs = acquireSyncs,
                PAcquireKeys = acquireKeys,
                PAcquireTimeouts = acquireTimeouts,
                ReleaseCount = 1,
                PReleaseSyncs = releaseSyncs,
                PReleaseKeys = releaseKeys
            };

            submitPNext = &keyedMutexInfo;
        }

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            PNext = submitPNext,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores
        };

        var submitResult = _vk.QueueSubmit(
            _graphicsQueue,
            1,
            &submitInfo,
            _inFlightFence);

        if (submitResult != Result.Success)
            throw new InvalidOperationException($"QueueSubmit failed: {submitResult}");

        SwapchainKHR* swapchains = stackalloc SwapchainKHR[] { _swapchain };
        uint* imageIndices = stackalloc uint[] { imageIndex };

        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapchains,
            PImageIndices = imageIndices
        };

        var presentResult = _khrSwapchain.QueuePresent(_presentQueue, &presentInfo);

        if (presentResult != Result.Success && presentResult != Result.SuboptimalKhr)
        {
            if (presentResult == Result.ErrorOutOfDateKhr)
                return;

            throw new InvalidOperationException($"QueuePresent failed: {presentResult}");
        }
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        if (!_initialized)
            return;

        if (width <= 0 || height <= 0)
            return;

        _vk.DeviceWaitIdle(_device);

        DestroySwapchain();

        CreateSwapchain(width, height);

        Array.Clear(_swapchainImageInitialized);
    }

    public void SetSourceD3D11SharedTexture(nint sharedHandle, uint width, uint height)
    {
        ThrowIfDisposed();

        if (!_initialized)
            return;

        if (sharedHandle == 0 || width <= 0 || height <= 0)
            return;

        uint newWidth = width;
        uint newHeight = height;

        if (_sourceSharedHandle == sharedHandle &&
            _sourceWidth == newWidth &&
            _sourceHeight == newHeight &&
            _sourceImage.Handle != 0)
        {
            return;
        }

        _vk.DeviceWaitIdle(_device);

        DestroySourceImage();

        ImportD3D11TextureAsVulkanImage(sharedHandle, newWidth, newHeight);
    }
    public void ClearSource()
    {
        if (!_initialized)
            return;

        _vk.DeviceWaitIdle(_device);
        DestroySourceImage();
    }

    private void ImportD3D11TextureAsVulkanImage(nint sharedHandle, uint width, uint height)
    {
        var externalMemoryImageCreateInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = ExternalMemoryHandleTypeFlags.D3D11TextureBit
        };

        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalMemoryImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage =
                ImageUsageFlags.TransferSrcBit |
                ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var imageResult = _vk.CreateImage(
            _device,
            &imageCreateInfo,
            null,
            out _sourceImage);

        if (imageResult != Result.Success)
            throw new InvalidOperationException($"Create imported Vulkan image failed: {imageResult}");

        _vk.GetImageMemoryRequirements(_device, _sourceImage, out MemoryRequirements memoryRequirements);

        var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
        {
            SType = StructureType.ImportMemoryWin32HandleInfoKhr,
            HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
            Handle = sharedHandle
        };

        var dedicatedAllocateInfo = new MemoryDedicatedAllocateInfo
        {
            SType = StructureType.MemoryDedicatedAllocateInfo,
            PNext = &importMemoryInfo,
            Image = _sourceImage,
            Buffer = default
        };

        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = &dedicatedAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(
                memoryRequirements.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit)
        };

        var allocResult = _vk.AllocateMemory(
            _device,
            &allocateInfo,
            null,
            out _sourceMemory);

        if (allocResult != Result.Success)
            throw new InvalidOperationException($"Allocate imported memory failed: {allocResult}");

        var bindResult = _vk.BindImageMemory(_device, _sourceImage, _sourceMemory, 0);

        if (bindResult != Result.Success)
            throw new InvalidOperationException($"Bind imported image memory failed: {bindResult}");

        _sourceSharedHandle = sharedHandle;
        _sourceWidth = width;
        _sourceHeight = height;
        _sourceImageInGeneralLayout = false;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out PhysicalDeviceMemoryProperties memoryProperties);

        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            bool typeMatches = (typeFilter & (1u << (int)i)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties;

            if (typeMatches && propertiesMatch)
                return i;
        }

        throw new InvalidOperationException("No suitable Vulkan memory type found.");
    }

    private void DestroySourceImage()
    {
        if (_sourceImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _sourceImage, null);
            _sourceImage = default;
        }

        if (_sourceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sourceMemory, null);
            _sourceMemory = default;
        }

        _sourceSharedHandle = 0;
        _sourceWidth = 0;
        _sourceHeight = 0;
        _sourceImageInGeneralLayout = false;
    }

    private void CreateInstance()
    {
        nint appName = SilkMarshal.StringToPtr("WTK MediaForge");
        nint engineName = SilkMarshal.StringToPtr("WTK MediaForge Vulkan");

        nint extSurface = SilkMarshal.StringToPtr(KhrSurface.ExtensionName);
        nint extWin32Surface = SilkMarshal.StringToPtr(KhrWin32Surface.ExtensionName);

        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = new Version32(0, 1, 0),
                PEngineName = (byte*)engineName,
                EngineVersion = new Version32(0, 1, 0),
                ApiVersion = Vk.Version12
            };

            byte** extensionNames = stackalloc byte*[2];
            extensionNames[0] = (byte*)extSurface;
            extensionNames[1] = (byte*)extWin32Surface;

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = 2,
                PpEnabledExtensionNames = extensionNames
            };

            var result = _vk.CreateInstance(&createInfo, null, out _instance);

            if (result != Result.Success)
                throw new InvalidOperationException($"vkCreateInstance failed: {result}");

            if (!_vk.TryGetInstanceExtension<KhrSurface>(_instance, out _khrSurface))
                throw new InvalidOperationException("KHR_surface extension was not loaded.");

            if (!_vk.TryGetInstanceExtension<KhrWin32Surface>(_instance, out _khrWin32Surface))
                throw new InvalidOperationException("KHR_win32_surface extension was not loaded.");
        }
        finally
        {
            SilkMarshal.FreeString(appName);
            SilkMarshal.FreeString(engineName);
            SilkMarshal.FreeString(extSurface);
            SilkMarshal.FreeString(extWin32Surface);
        }
    }

    private void CreateSurface(IntPtr hwnd)
    {
        if (_khrWin32Surface is null)
            throw new InvalidOperationException("KHR_win32_surface is not available.");

        IntPtr hinstance = GetModuleHandle(null);

        var createInfo = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hinstance = hinstance,
            Hwnd = hwnd
        };

        var result = _khrWin32Surface.CreateWin32Surface(
            _instance,
            &createInfo,
            null,
            out _surface);

        if (result != Result.Success)
            throw new InvalidOperationException($"vkCreateWin32SurfaceKHR failed: {result}");
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        PhysicalDevice* devices = stackalloc PhysicalDevice[(int)deviceCount];
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);

        for (uint i = 0; i < deviceCount; i++)
        {
            var candidate = devices[i];

            if (TryFindQueueFamilies(candidate, out uint graphicsFamily, out uint presentFamily) &&
                SupportsSwapchain(candidate))
            {
                _physicalDevice = candidate;
                _graphicsQueueFamilyIndex = graphicsFamily;
                _presentQueueFamilyIndex = presentFamily;
                return;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan physical device found.");
    }

    private bool TryFindQueueFamilies(
        PhysicalDevice device,
        out uint graphicsFamily,
        out uint presentFamily)
    {
        graphicsFamily = uint.MaxValue;
        presentFamily = uint.MaxValue;

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

        QueueFamilyProperties* families =
            stackalloc QueueFamilyProperties[(int)queueFamilyCount];

        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, families);

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            bool supportsGraphics =
                (families[i].QueueFlags & QueueFlags.GraphicsBit) != 0;

            Bool32 supportsPresent = false;

            _khrSurface!.GetPhysicalDeviceSurfaceSupport(
                device,
                i,
                _surface,
                &supportsPresent);

            if (supportsGraphics && graphicsFamily == uint.MaxValue)
                graphicsFamily = i;

            if (supportsPresent && presentFamily == uint.MaxValue)
                presentFamily = i;

            if (graphicsFamily != uint.MaxValue && presentFamily != uint.MaxValue)
                return true;
        }

        return false;
    }

    private bool SupportsSwapchain(PhysicalDevice device)
    {
        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, null);

        if (extensionCount == 0)
            return false;

        ExtensionProperties* extensions =
            stackalloc ExtensionProperties[(int)extensionCount];

        _vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, extensions);

        for (uint i = 0; i < extensionCount; i++)
        {
            string? name = Marshal.PtrToStringAnsi((IntPtr)extensions[i].ExtensionName);

            if (name == KhrSwapchain.ExtensionName)
                return true;
        }

        return false;
    }

    private void CreateLogicalDevice()
    {
        float priority = 1.0f;

        Span<uint> uniqueFamilies = _graphicsQueueFamilyIndex == _presentQueueFamilyIndex
            ? stackalloc uint[] { _graphicsQueueFamilyIndex }
            : stackalloc uint[] { _graphicsQueueFamilyIndex, _presentQueueFamilyIndex };

        DeviceQueueCreateInfo* queueCreateInfos =
            stackalloc DeviceQueueCreateInfo[uniqueFamilies.Length];

        for (int i = 0; i < uniqueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &priority
            };
        }

        nint swapchainExtensionName = SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName);
        nint externalMemoryExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory");
        nint externalMemoryWin32ExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory_win32");
        nint win32KeyedMutexExtensionName = SilkMarshal.StringToPtr("VK_KHR_win32_keyed_mutex");

        try
        {
            byte** enabledExtensions = stackalloc byte*[4];
            enabledExtensions[0] = (byte*)swapchainExtensionName;
            enabledExtensions[1] = (byte*)externalMemoryExtensionName;
            enabledExtensions[2] = (byte*)externalMemoryWin32ExtensionName;
            enabledExtensions[3] = (byte*)win32KeyedMutexExtensionName;

            var features = new PhysicalDeviceFeatures();

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueFamilies.Length,
                PQueueCreateInfos = queueCreateInfos,
                EnabledExtensionCount = 4,
                PpEnabledExtensionNames = enabledExtensions,
                PEnabledFeatures = &features
            };

            var result = _vk.CreateDevice(_physicalDevice, &createInfo, null, out _device);

            if (result != Result.Success)
                throw new InvalidOperationException($"vkCreateDevice failed: {result}");
        }
        finally
        {
            SilkMarshal.FreeString(swapchainExtensionName);
            SilkMarshal.FreeString(externalMemoryExtensionName);
            SilkMarshal.FreeString(externalMemoryWin32ExtensionName);
            SilkMarshal.FreeString(win32KeyedMutexExtensionName);
        }

        _vk.GetDeviceQueue(_device, _graphicsQueueFamilyIndex, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamilyIndex, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension<KhrSwapchain>(
                _instance,
                _device,
                out _khrSwapchain))
        {
            throw new InvalidOperationException("KHR_swapchain extension was not loaded.");
        }
    }

    private void CreateSwapchain(int width, int height)
    {
        _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(
            _physicalDevice,
            _surface,
            out SurfaceCapabilitiesKHR capabilities);

        SurfaceFormatKHR surfaceFormat = ChooseSurfaceFormat();
        PresentModeKHR presentMode = ChoosePresentMode();

        _swapchainImageFormat = surfaceFormat.Format;
        _swapchainExtent = ChooseSwapExtent(capabilities, width, height);

        uint imageCount = capabilities.MinImageCount + 1;

        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        ImageUsageFlags imageUsage =
            ImageUsageFlags.TransferDstBit |
            ImageUsageFlags.ColorAttachmentBit;

        if ((capabilities.SupportedUsageFlags & imageUsage) != imageUsage)
        {
            imageUsage = ImageUsageFlags.ColorAttachmentBit;
        }

        uint* queueFamilyIndices = stackalloc uint[]
        {
            _graphicsQueueFamilyIndex,
            _presentQueueFamilyIndex
        };

        var sharingMode =
            _graphicsQueueFamilyIndex == _presentQueueFamilyIndex
                ? SharingMode.Exclusive
                : SharingMode.Concurrent;

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _swapchainImageFormat,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = imageUsage,
            ImageSharingMode = sharingMode,
            QueueFamilyIndexCount = sharingMode == SharingMode.Concurrent ? 2u : 0u,
            PQueueFamilyIndices = sharingMode == SharingMode.Concurrent ? queueFamilyIndices : null,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        var result = _khrSwapchain!.CreateSwapchain(
            _device,
            &createInfo,
            null,
            out _swapchain);

        if (result != Result.Success)
            throw new InvalidOperationException($"CreateSwapchain failed: {result}");

        uint actualImageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &actualImageCount, null);

        Image* images = stackalloc Image[(int)actualImageCount];
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &actualImageCount, images);

        _swapchainImages = new Image[actualImageCount];
        _swapchainImageInitialized = new bool[actualImageCount];

        for (int i = 0; i < actualImageCount; i++)
            _swapchainImages[i] = images[i];
    }

    private SurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint count = 0;
        _khrSurface!.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &count, null);

        if (count == 0)
            throw new InvalidOperationException("No surface formats available.");

        SurfaceFormatKHR* formats = stackalloc SurfaceFormatKHR[(int)count];

        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &count, formats);

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Srgb &&
                formats[i].ColorSpace == ColorSpaceKHR.PaceSrgbNonlinearKhr)
            {
                return formats[i];
            }
        }

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Unorm &&
                formats[i].ColorSpace == ColorSpaceKHR.PaceSrgbNonlinearKhr)
            {
                return formats[i];
            }
        }

        return formats[0];
    }

    private PresentModeKHR ChoosePresentMode()
    {
        uint count = 0;
        _khrSurface!.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &count, null);

        if (count == 0)
            return PresentModeKHR.FifoKhr;

        PresentModeKHR* modes = stackalloc PresentModeKHR[(int)count];

        _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &count, modes);

        for (uint i = 0; i < count; i++)
        {
            if (modes[i] == PresentModeKHR.MailboxKhr)
                return PresentModeKHR.MailboxKhr;
        }

        return PresentModeKHR.FifoKhr;
    }

    private static Extent2D ChooseSwapExtent(
        SurfaceCapabilitiesKHR capabilities,
        int width,
        int height)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return capabilities.CurrentExtent;

        uint actualWidth = (uint)Math.Max(1, width);
        uint actualHeight = (uint)Math.Max(1, height);

        actualWidth = Math.Clamp(
            actualWidth,
            capabilities.MinImageExtent.Width,
            capabilities.MaxImageExtent.Width);

        actualHeight = Math.Clamp(
            actualHeight,
            capabilities.MinImageExtent.Height,
            capabilities.MaxImageExtent.Height);

        return new Extent2D(actualWidth, actualHeight);
    }

    private void CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        var result = _vk.CreateCommandPool(_device, &createInfo, null, out _commandPool);

        if (result != Result.Success)
            throw new InvalidOperationException($"CreateCommandPool failed: {result}");
    }

    private void CreateCommandBuffer()
    {
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        var result = _vk.AllocateCommandBuffers(_device, &allocateInfo, out _commandBuffer);

        if (result != Result.Success)
            throw new InvalidOperationException($"AllocateCommandBuffers failed: {result}");
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _imageAvailableSemaphore) != Result.Success)
            throw new InvalidOperationException("Failed to create image available semaphore.");

        if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _renderFinishedSemaphore) != Result.Success)
            throw new InvalidOperationException("Failed to create render finished semaphore.");

        if (_vk.CreateFence(_device, &fenceInfo, null, out _inFlightFence) != Result.Success)
            throw new InvalidOperationException("Failed to create in-flight fence.");
    }

    private void RecordCopyOrClearCommandBuffer(uint imageIndex)
    {
        _vk.ResetCommandBuffer(_commandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        var beginResult = _vk.BeginCommandBuffer(_commandBuffer, &beginInfo);

        if (beginResult != Result.Success)
            throw new InvalidOperationException($"BeginCommandBuffer failed: {beginResult}");

        ImageLayout oldSwapchainLayout = _swapchainImageInitialized[imageIndex]
            ? ImageLayout.PresentSrcKhr
            : ImageLayout.Undefined;

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            oldSwapchainLayout,
            ImageLayout.TransferDstOptimal);

        _swapchainImageInitialized[imageIndex] = true;

        if (_sourceImage.Handle != 0)
        {
            if (!_sourceImageInGeneralLayout)
            {
                TransitionImageLayout(
                    _sourceImage,
                    ImageLayout.Undefined,
                    ImageLayout.General);

                _sourceImageInGeneralLayout = true;
            }

            uint copyWidth = Math.Min(_sourceWidth, _swapchainExtent.Width);
            uint copyHeight = Math.Min(_sourceHeight, _swapchainExtent.Height);

            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D(copyWidth, copyHeight, 1)
            };

            _vk.CmdCopyImage(
                _commandBuffer,
                _sourceImage,
                ImageLayout.General,
                _swapchainImages[imageIndex],
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        }
        else
        {
            var clearColor = new ClearColorValue
            {
                Float32_0 = 0.70f,
                Float32_1 = 0.00f,
                Float32_2 = 0.00f,
                Float32_3 = 1.00f
            };

            var range = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            _vk.CmdClearColorImage(
                _commandBuffer,
                _swapchainImages[imageIndex],
                ImageLayout.TransferDstOptimal,
                &clearColor,
                1,
                &range);
        }

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            ImageLayout.TransferDstOptimal,
            ImageLayout.PresentSrcKhr);

        var endResult = _vk.EndCommandBuffer(_commandBuffer);

        if (endResult != Result.Success)
            throw new InvalidOperationException($"EndCommandBuffer failed: {endResult}");
    }

    private void RecordClearCommandBuffer(uint imageIndex)
    {
        _vk.ResetCommandBuffer(_commandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        var beginResult = _vk.BeginCommandBuffer(_commandBuffer, &beginInfo);

        if (beginResult != Result.Success)
            throw new InvalidOperationException($"BeginCommandBuffer failed: {beginResult}");

        ImageLayout oldLayout = _swapchainImageInitialized[imageIndex]
            ? ImageLayout.PresentSrcKhr
            : ImageLayout.Undefined;

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            oldLayout,
            ImageLayout.TransferDstOptimal);

        _swapchainImageInitialized[imageIndex] = true;

        var clearColor = new ClearColorValue
        {
            Float32_0 = 0.06f,
            Float32_1 = 0.08f,
            Float32_2 = 0.13f,
            Float32_3 = 1.00f
        };

        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        _vk.CmdClearColorImage(
            _commandBuffer,
            _swapchainImages[imageIndex],
            ImageLayout.TransferDstOptimal,
            &clearColor,
            1,
            &range);

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            ImageLayout.TransferDstOptimal,
            ImageLayout.PresentSrcKhr);

        var endResult = _vk.EndCommandBuffer(_commandBuffer);

        if (endResult != Result.Success)
            throw new InvalidOperationException($"EndCommandBuffer failed: {endResult}");
    }

    private void TransitionImageLayout(
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined &&
            newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Undefined &&
            newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.PresentSrcKhr &&
                 newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.MemoryReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;

            sourceStage = PipelineStageFlags.BottomOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal &&
                 newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;

            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.BottomOfPipeBit;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported layout transition: {oldLayout} -> {newLayout}");
        }

        _vk.CmdPipelineBarrier(
            _commandBuffer,
            sourceStage,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

    private string GetSelectedGpuDescription()
    {
        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);

        string deviceName =
            Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ??
            "Unknown Vulkan GPU";

        return $"{deviceName} | Type: {properties.DeviceType}";
    }

    private void DestroySwapchain()
    {
        if (_swapchain.Handle != 0 && _khrSwapchain is not null)
        {
            _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }

        _swapchainImages = Array.Empty<Image>();
        _swapchainImageInitialized = Array.Empty<bool>();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_device.Handle != 0)
            _vk.DeviceWaitIdle(_device);

        if (_inFlightFence.Handle != 0)
            _vk.DestroyFence(_device, _inFlightFence, null);

        if (_renderFinishedSemaphore.Handle != 0)
            _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);

        if (_imageAvailableSemaphore.Handle != 0)
            _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);

        if (_commandPool.Handle != 0)
            _vk.DestroyCommandPool(_device, _commandPool, null);

        DestroySourceImage();

        DestroySwapchain();

        if (_device.Handle != 0)
            _vk.DestroyDevice(_device, null);

        if (_surface.Handle != 0 && _khrSurface is not null)
            _khrSurface.DestroySurface(_instance, _surface, null);

        if (_instance.Handle != 0)
            _vk.DestroyInstance(_instance, null);

        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VulkanPreviewRenderer));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}