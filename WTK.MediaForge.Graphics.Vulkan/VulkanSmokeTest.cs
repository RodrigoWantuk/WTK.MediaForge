using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed unsafe class VulkanSmokeTest : IDisposable
{
    private readonly Vk _vk;

    private Instance _instance;
    private SurfaceKHR _surface;

    private KhrSurface? _khrSurface;
    private KhrWin32Surface? _khrWin32Surface;

    private bool _initialized;
    private bool _disposed;

    public VulkanSmokeTest()
    {
        _vk = Vk.GetApi();
    }

    public string InitializeForWin32Panel(IntPtr hwnd)
    {
        ThrowIfDisposed();

        if (_initialized)
            return "Vulkan smoke test already initialized.";

        CreateInstance();
        CreateWin32Surface(hwnd);

        string gpuInfo = ProbePhysicalDevices();

        _initialized = true;

        return gpuInfo;
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

    private void CreateWin32Surface(IntPtr hwnd)
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

    private string ProbePhysicalDevices()
    {
        if (_khrSurface is null)
            throw new InvalidOperationException("KHR_surface is not available.");

        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        var devices = stackalloc PhysicalDevice[(int)deviceCount];
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);

        var lines = new List<string>();

        for (uint i = 0; i < deviceCount; i++)
        {
            var device = devices[i];

            _vk.GetPhysicalDeviceProperties(device, out var properties);

            string deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ?? "Unknown Vulkan GPU";

            uint queueFamilyCount = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

            var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies);

            bool hasGraphicsPresentQueue = false;

            for (uint queueIndex = 0; queueIndex < queueFamilyCount; queueIndex++)
            {
                bool supportsGraphics =
                    (queueFamilies[queueIndex].QueueFlags & QueueFlags.GraphicsBit) != 0;

                Bool32 supportsPresent = false;

                _khrSurface.GetPhysicalDeviceSurfaceSupport(
                    device,
                    queueIndex,
                    _surface,
                    &supportsPresent);

                if (supportsGraphics && supportsPresent)
                {
                    hasGraphicsPresentQueue = true;
                    break;
                }
            }

            lines.Add(
                $"{i}: {deviceName} | " +
                $"Type: {properties.DeviceType} | " +
                $"Graphics+Present: {(hasGraphicsPresentQueue ? "YES" : "NO")}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_surface.Handle != 0 && _khrSurface is not null)
        {
            _khrSurface.DestroySurface(_instance, _surface, null);
            _surface = default;
        }

        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
            _instance = default;
        }

        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VulkanSmokeTest));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}