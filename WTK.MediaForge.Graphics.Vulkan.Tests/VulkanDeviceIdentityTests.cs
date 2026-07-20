using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public sealed class VulkanDeviceIdentityTests
{
    [Fact]
    public void Windows_vulkan_device_exposes_dxgi_adapter_luid()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var device = VulkanHeadlessDevice.Create();

        Assert.False(string.IsNullOrWhiteSpace(device.DeviceName));
        Assert.True(device.DeviceLuidValid, $"Vulkan device '{device.DeviceName}' did not expose a valid Windows LUID.");
        Assert.False(device.DeviceLuid.IsEmpty);
    }
}
