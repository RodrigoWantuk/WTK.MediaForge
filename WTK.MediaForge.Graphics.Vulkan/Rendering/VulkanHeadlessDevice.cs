using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Capture;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanHeadlessDevice : IDisposable
{
    private readonly object _commandQueueGate = new();
    private readonly object _auxiliaryCommandPoolGate = new();
    private readonly Vk _vk;
    private bool _disposed;

    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private uint _graphicsQueueFamilyIndex;
    private CommandPool _commandPool;
    private CommandPool _auxiliaryCommandPool;
    private KhrSurface? _khrSurface;
    private KhrWin32Surface? _khrWin32Surface;
    private KhrSwapchain? _khrSwapchain;
    private readonly string _deviceName;
    private readonly GpuAdapterLuid _deviceLuid;
    private readonly bool _deviceLuidValid;

    private VulkanHeadlessDevice(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue graphicsQueue,
        uint graphicsQueueFamilyIndex,
        CommandPool commandPool,
        CommandPool auxiliaryCommandPool,
        KhrSurface? khrSurface,
        KhrWin32Surface? khrWin32Surface,
        KhrSwapchain? khrSwapchain,
        string deviceName,
        GpuAdapterLuid deviceLuid,
        bool deviceLuidValid)
    {
        _vk = vk;
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _graphicsQueue = graphicsQueue;
        _graphicsQueueFamilyIndex = graphicsQueueFamilyIndex;
        _commandPool = commandPool;
        _auxiliaryCommandPool = auxiliaryCommandPool;
        _khrSurface = khrSurface;
        _khrWin32Surface = khrWin32Surface;
        _khrSwapchain = khrSwapchain;
        _deviceName = deviceName;
        _deviceLuid = deviceLuid;
        _deviceLuidValid = deviceLuidValid;
    }

    public Instance Instance => _instance;

    public uint GraphicsQueueFamilyIndex => _graphicsQueueFamilyIndex;

    public bool SupportsWin32Presentation =>
        OperatingSystem.IsWindows() &&
        _khrSurface is not null &&
        _khrWin32Surface is not null &&
        _khrSwapchain is not null;

    public KhrSurface? KhrSurface => _khrSurface;

    public KhrWin32Surface? KhrWin32Surface => _khrWin32Surface;

    public KhrSwapchain? KhrSwapchain => _khrSwapchain;

    public Vk Vk => _vk;

    public PhysicalDevice PhysicalDevice => _physicalDevice;

    public Device Device => _device;

    public Queue GraphicsQueue => _graphicsQueue;

    public CommandPool CommandPool => _commandPool;

    public object CommandQueueGate => _commandQueueGate;

    public object AuxiliaryCommandPoolGate => _auxiliaryCommandPoolGate;

    public string DeviceName => _deviceName;

    public GpuAdapterLuid DeviceLuid => _deviceLuid;

    public bool DeviceLuidValid => _deviceLuidValid;

    public CommandBuffer AllocateAndBeginPrimaryCommandBuffer(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        lock (_commandQueueGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AllocateAndBeginCommandBuffer(_commandPool, operationName);
        }
    }

    public void FreePrimaryCommandBuffer(CommandBuffer commandBuffer)
    {
        if (commandBuffer.Handle == 0)
            return;

        lock (_commandQueueGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        }
    }

    public CommandBuffer AllocateAndBeginAuxiliaryCommandBuffer(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        lock (_auxiliaryCommandPoolGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AllocateAndBeginCommandBuffer(_auxiliaryCommandPool, operationName);
        }
    }

    public void FreeAuxiliaryCommandBuffer(CommandBuffer commandBuffer)
    {
        if (commandBuffer.Handle == 0)
            return;

        lock (_auxiliaryCommandPoolGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _vk.FreeCommandBuffers(_device, _auxiliaryCommandPool, 1, &commandBuffer);
        }
    }

    public static VulkanHeadlessDevice Create()
    {
        var vk = Vk.GetApi();
        CreateInstance(vk, out var instance);
        PickPhysicalDevice(vk, instance, out var physicalDevice, out var graphicsQueueFamilyIndex);
        CreateLogicalDevice(vk, physicalDevice, graphicsQueueFamilyIndex, out var device, out var graphicsQueue);
        CreateCommandPool(vk, device, graphicsQueueFamilyIndex, out var commandPool);
        CreateCommandPool(vk, device, graphicsQueueFamilyIndex, out var auxiliaryCommandPool);
        LoadPresentationExtensions(vk, instance, device, out var khrSurface, out var khrWin32Surface, out var khrSwapchain);
        GetPhysicalDeviceIdentity(vk, physicalDevice, out var deviceName, out var deviceLuid, out var deviceLuidValid);

        return new VulkanHeadlessDevice(
            vk,
            instance,
            physicalDevice,
            device,
            graphicsQueue,
            graphicsQueueFamilyIndex,
            commandPool,
            auxiliaryCommandPool,
            khrSurface,
            khrWin32Surface,
            khrSwapchain,
            deviceName,
            deviceLuid,
            deviceLuidValid);
    }

    private static void GetPhysicalDeviceIdentity(
        Vk vk,
        PhysicalDevice physicalDevice,
        out string deviceName,
        out GpuAdapterLuid deviceLuid,
        out bool deviceLuidValid)
    {
        var idProperties = new PhysicalDeviceIDProperties
        {
            SType = StructureType.PhysicalDeviceIDProperties
        };
        var properties = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &idProperties
        };

        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties);
        deviceName = Marshal.PtrToStringAnsi((nint)properties.Properties.DeviceName) ?? "Unknown Vulkan GPU";
        deviceLuidValid = idProperties.DeviceLuidvalid;
        if (!deviceLuidValid)
        {
            deviceLuid = GpuAdapterLuid.Empty;
            return;
        }

        var lowPart =
            (uint)idProperties.DeviceLuid[0] |
            ((uint)idProperties.DeviceLuid[1] << 8) |
            ((uint)idProperties.DeviceLuid[2] << 16) |
            ((uint)idProperties.DeviceLuid[3] << 24);
        var highPart =
            idProperties.DeviceLuid[4] |
            (idProperties.DeviceLuid[5] << 8) |
            (idProperties.DeviceLuid[6] << 16) |
            (idProperties.DeviceLuid[7] << 24);
        deviceLuid = new GpuAdapterLuid { LowPart = lowPart, HighPart = highPart };
    }

    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out PhysicalDeviceMemoryProperties memoryProperties);

        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable Vulkan memory type.");
    }

    public void Dispose()
    {
        lock (_auxiliaryCommandPoolGate)
        lock (_commandQueueGate)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_auxiliaryCommandPool.Handle != 0)
            {
                _vk.DestroyCommandPool(_device, _auxiliaryCommandPool, null);
                _auxiliaryCommandPool = default;
            }

            if (_commandPool.Handle != 0)
            {
                _vk.DestroyCommandPool(_device, _commandPool, null);
                _commandPool = default;
            }

            if (_device.Handle != 0)
            {
                _vk.DestroyDevice(_device, null);
                _device = default;
                _graphicsQueue = default;
            }

            if (_instance.Handle != 0)
            {
                _vk.DestroyInstance(_instance, null);
                _instance = default;
            }

            _vk.Dispose();
        }
    }

    private CommandBuffer AllocateAndBeginCommandBuffer(
        CommandPool commandPool,
        string operationName)
    {
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (_vk.AllocateCommandBuffers(_device, &allocateInfo, out var commandBuffer) != Result.Success)
        {
            throw new InvalidOperationException(
                $"vkAllocateCommandBuffers failed for {operationName}.");
        }

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        var beginResult = _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        if (beginResult == Result.Success)
            return commandBuffer;

        _vk.FreeCommandBuffers(_device, commandPool, 1, &commandBuffer);
        throw new InvalidOperationException(
            $"vkBeginCommandBuffer failed for {operationName}: {beginResult}.");
    }

    private static void CreateInstance(Vk vk, out Instance instance)
    {
        nint appName = SilkMarshal.StringToPtr("WTK MediaForge");
        nint engineName = SilkMarshal.StringToPtr("WTK MediaForge Vulkan");

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

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo
            };

            if (OperatingSystem.IsWindows())
            {
                nint extSurface = SilkMarshal.StringToPtr(KhrSurface.ExtensionName);
                nint extWin32Surface = SilkMarshal.StringToPtr(KhrWin32Surface.ExtensionName);

                try
                {
                    byte** extensionNames = stackalloc byte*[2];
                    extensionNames[0] = (byte*)extSurface;
                    extensionNames[1] = (byte*)extWin32Surface;
                    createInfo.EnabledExtensionCount = 2;
                    createInfo.PpEnabledExtensionNames = extensionNames;

                    if (vk.CreateInstance(&createInfo, null, out instance) != Result.Success)
                        throw new InvalidOperationException("vkCreateInstance failed.");
                }
                finally
                {
                    SilkMarshal.FreeString(extSurface);
                    SilkMarshal.FreeString(extWin32Surface);
                }
            }
            else if (vk.CreateInstance(&createInfo, null, out instance) != Result.Success)
            {
                throw new InvalidOperationException("vkCreateInstance failed.");
            }
        }
        finally
        {
            SilkMarshal.FreeString(appName);
            SilkMarshal.FreeString(engineName);
        }
    }

    private static void PickPhysicalDevice(
        Vk vk,
        Instance instance,
        out PhysicalDevice physicalDevice,
        out uint graphicsQueueFamilyIndex)
    {
        uint deviceCount = 0;
        vk.EnumeratePhysicalDevices(instance, &deviceCount, null);

        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        PhysicalDevice* devices = stackalloc PhysicalDevice[(int)deviceCount];
        vk.EnumeratePhysicalDevices(instance, &deviceCount, devices);

        for (uint i = 0; i < deviceCount; i++)
        {
            var candidate = devices[i];

            if (TryFindGraphicsQueueFamily(vk, candidate, out graphicsQueueFamilyIndex) &&
                SupportsRequiredDeviceExtensions(vk, candidate))
            {
                physicalDevice = candidate;
                return;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan physical device found.");
    }

    private static bool TryFindGraphicsQueueFamily(
        Vk vk,
        PhysicalDevice device,
        out uint graphicsQueueFamilyIndex)
    {
        graphicsQueueFamilyIndex = uint.MaxValue;

        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

        QueueFamilyProperties* families = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
        vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, families);

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((families[i].QueueFlags & QueueFlags.GraphicsBit) == 0)
                continue;

            graphicsQueueFamilyIndex = i;
            return true;
        }

        return false;
    }

    private static bool SupportsRequiredDeviceExtensions(Vk vk, PhysicalDevice device)
    {
        var required = OperatingSystem.IsWindows()
            ? new[]
            {
                "VK_KHR_external_memory",
                "VK_KHR_external_memory_win32",
                "VK_KHR_win32_keyed_mutex",
                "VK_KHR_swapchain"
            }
            : new[]
            {
                "VK_KHR_external_memory",
                "VK_KHR_external_memory_win32",
                "VK_KHR_win32_keyed_mutex"
            };

        uint extensionCount = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, null);

        if (extensionCount == 0)
            return false;

        ExtensionProperties* extensions = stackalloc ExtensionProperties[(int)extensionCount];
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, extensions);

        var found = 0;

        for (uint i = 0; i < extensionCount; i++)
        {
            string? name = Marshal.PtrToStringAnsi((IntPtr)extensions[i].ExtensionName);
            if (name is null)
                continue;

            foreach (var requiredName in required)
            {
                if (name == requiredName)
                    found++;
            }
        }

        return found == required.Length;
    }

    private static void CreateLogicalDevice(
        Vk vk,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamilyIndex,
        out Device device,
        out Queue graphicsQueue)
    {
        float priority = 1.0f;

        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = graphicsQueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &priority
        };

        nint externalMemoryExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory");
        nint externalMemoryWin32ExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory_win32");
        nint win32KeyedMutexExtensionName = SilkMarshal.StringToPtr("VK_KHR_win32_keyed_mutex");
        nint? swapchainExtensionName = OperatingSystem.IsWindows()
            ? SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName)
            : null;

        try
        {
            var extensionCount = OperatingSystem.IsWindows() ? 4 : 3;
            byte** enabledExtensions = stackalloc byte*[4];
            enabledExtensions[0] = (byte*)externalMemoryExtensionName;
            enabledExtensions[1] = (byte*)externalMemoryWin32ExtensionName;
            enabledExtensions[2] = (byte*)win32KeyedMutexExtensionName;
            if (swapchainExtensionName is not null)
                enabledExtensions[3] = (byte*)swapchainExtensionName.Value;

            var features = new PhysicalDeviceFeatures();

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,
                EnabledExtensionCount = (uint)extensionCount,
                PpEnabledExtensionNames = enabledExtensions,
                PEnabledFeatures = &features
            };

            if (vk.CreateDevice(physicalDevice, &createInfo, null, out device) != Result.Success)
                throw new InvalidOperationException("vkCreateDevice failed.");

            vk.GetDeviceQueue(device, graphicsQueueFamilyIndex, 0, out graphicsQueue);
        }
        finally
        {
            SilkMarshal.FreeString(externalMemoryExtensionName);
            SilkMarshal.FreeString(externalMemoryWin32ExtensionName);
            SilkMarshal.FreeString(win32KeyedMutexExtensionName);
            if (swapchainExtensionName is not null)
                SilkMarshal.FreeString(swapchainExtensionName.Value);
        }
    }

    private static void LoadPresentationExtensions(
        Vk vk,
        Instance instance,
        Device device,
        out KhrSurface? khrSurface,
        out KhrWin32Surface? khrWin32Surface,
        out KhrSwapchain? khrSwapchain)
    {
        khrSurface = null;
        khrWin32Surface = null;
        khrSwapchain = null;

        if (!OperatingSystem.IsWindows())
            return;

        if (!vk.TryGetInstanceExtension(instance, out khrSurface))
            throw new InvalidOperationException("KHR_surface extension was not loaded.");

        if (!vk.TryGetInstanceExtension(instance, out khrWin32Surface))
            throw new InvalidOperationException("KHR_win32_surface extension was not loaded.");

        if (!vk.TryGetDeviceExtension(instance, device, out khrSwapchain))
            throw new InvalidOperationException("KHR_swapchain extension was not loaded.");
    }

    private static void CreateCommandPool(
        Vk vk,
        Device device,
        uint graphicsQueueFamilyIndex,
        out CommandPool commandPool)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = graphicsQueueFamilyIndex
        };

        if (vk.CreateCommandPool(device, &poolInfo, null, out commandPool) != Result.Success)
            throw new InvalidOperationException("vkCreateCommandPool failed.");
    }
}
