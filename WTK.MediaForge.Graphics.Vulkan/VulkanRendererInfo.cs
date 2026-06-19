using WTK.MediaForge.Core.Capture;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed class VulkanRendererInfo
{
    public required string DeviceName { get; init; }
    public required GpuAdapterLuid DeviceLuid { get; init; }
    public bool DeviceLuidValid { get; init; }
    public required string SwapchainFormat { get; init; }
    public required uint SwapchainWidth { get; init; }
    public required uint SwapchainHeight { get; init; }
    public int ResolvedShaderRotation { get; init; }
}
