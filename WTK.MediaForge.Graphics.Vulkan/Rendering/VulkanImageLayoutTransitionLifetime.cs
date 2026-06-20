using Silk.NET.Vulkan;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanImageLayoutTransitionLifetime
{
    public static int UndefinedToShaderReadTransitions;
    public static int GeneralToShaderReadTransitions;
    public static int ColorAttachmentToShaderReadTransitions;

    public static void Reset()
    {
        Volatile.Write(ref UndefinedToShaderReadTransitions, 0);
        Volatile.Write(ref GeneralToShaderReadTransitions, 0);
        Volatile.Write(ref ColorAttachmentToShaderReadTransitions, 0);
    }

    public static void Record(ImageLayout oldLayout, ImageLayout newLayout)
    {
        if (newLayout != ImageLayout.ShaderReadOnlyOptimal)
            return;

        if (oldLayout == ImageLayout.Undefined)
            Interlocked.Increment(ref UndefinedToShaderReadTransitions);
        else if (oldLayout == ImageLayout.General)
            Interlocked.Increment(ref GeneralToShaderReadTransitions);
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal)
            Interlocked.Increment(ref ColorAttachmentToShaderReadTransitions);
    }
}
