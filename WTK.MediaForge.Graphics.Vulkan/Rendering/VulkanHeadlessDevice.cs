using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanHeadlessDevice : IDisposable
{
    private readonly Vk _vk;
    private bool _disposed;

    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private uint _graphicsQueueFamilyIndex;
    private CommandPool _commandPool;

    private VulkanHeadlessDevice(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue graphicsQueue,
        uint graphicsQueueFamilyIndex,
        CommandPool commandPool)
    {
        _vk = vk;
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _graphicsQueue = graphicsQueue;
        _graphicsQueueFamilyIndex = graphicsQueueFamilyIndex;
        _commandPool = commandPool;
    }

    public Vk Vk => _vk;

    public PhysicalDevice PhysicalDevice => _physicalDevice;

    public Device Device => _device;

    public Queue GraphicsQueue => _graphicsQueue;

    public CommandPool CommandPool => _commandPool;

    public static VulkanHeadlessDevice Create()
    {
        var vk = Vk.GetApi();
        CreateInstance(vk, out var instance);
        PickPhysicalDevice(vk, instance, out var physicalDevice, out var graphicsQueueFamilyIndex);
        CreateLogicalDevice(vk, physicalDevice, graphicsQueueFamilyIndex, out var device, out var graphicsQueue);
        CreateCommandPool(vk, device, graphicsQueueFamilyIndex, out var commandPool);

        return new VulkanHeadlessDevice(
            vk,
            instance,
            physicalDevice,
            device,
            graphicsQueue,
            graphicsQueueFamilyIndex,
            commandPool);
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

    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _vk.DeviceWaitIdle(_device);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

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

            if (vk.CreateInstance(&createInfo, null, out instance) != Result.Success)
                throw new InvalidOperationException("vkCreateInstance failed.");
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
        ReadOnlySpan<string> required =
        [
            "VK_KHR_external_memory",
            "VK_KHR_external_memory_win32",
            "VK_KHR_win32_keyed_mutex"
        ];

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

        try
        {
            byte** enabledExtensions = stackalloc byte*[3];
            enabledExtensions[0] = (byte*)externalMemoryExtensionName;
            enabledExtensions[1] = (byte*)externalMemoryWin32ExtensionName;
            enabledExtensions[2] = (byte*)win32KeyedMutexExtensionName;

            var features = new PhysicalDeviceFeatures();

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,
                EnabledExtensionCount = 3,
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
        }
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
