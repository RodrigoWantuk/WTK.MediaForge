namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal readonly record struct VulkanReadbackFrame(byte[] Pixels, int StrideBytes);
