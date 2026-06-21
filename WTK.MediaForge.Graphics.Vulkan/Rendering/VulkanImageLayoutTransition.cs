using Silk.NET.Vulkan;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static unsafe class VulkanImageLayoutTransition
{
    public static void Transition(
        Vk vk,
        CommandBuffer commandBuffer,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout)
    {
        VulkanImageLayoutTransitionLifetime.Record(oldLayout, newLayout);

        var (sourceStage, destinationStage, srcAccess, dstAccess) = GetTransitionStages(oldLayout, newLayout);

        var barrier = CreateBarrier(image, oldLayout, newLayout, srcAccess, dstAccess);

        vk.CmdPipelineBarrier(
            commandBuffer,
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

    private static (PipelineStageFlags Source, PipelineStageFlags Destination, AccessFlags SrcAccess, AccessFlags DstAccess)
        GetTransitionStages(ImageLayout oldLayout, ImageLayout newLayout) =>
        (oldLayout, newLayout) switch
        {
            (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.FragmentShaderBit, 0, AccessFlags.ShaderReadBit),
            (ImageLayout.Undefined, ImageLayout.General) =>
                (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.FragmentShaderBit, 0, AccessFlags.ShaderReadBit),
            (ImageLayout.General, ImageLayout.General) =>
                (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit, AccessFlags.ShaderReadBit),
            (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) or
            (ImageLayout.General, ImageLayout.ColorAttachmentOptimal) =>
                (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit, 0, AccessFlags.ColorAttachmentWriteBit),
            (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit),
            (ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit, AccessFlags.ShaderReadBit),
            (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal) =>
                (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ShaderReadBit, AccessFlags.ColorAttachmentWriteBit),
            (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal) =>
                (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.TransferBit, AccessFlags.ShaderReadBit, AccessFlags.TransferReadBit),
            (ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal) =>
                (PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit, AccessFlags.ColorAttachmentWriteBit, AccessFlags.TransferReadBit),
            (ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.TransferReadBit, AccessFlags.ShaderReadBit),
            (ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal) =>
                (PipelineStageFlags.TransferBit, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.TransferReadBit, AccessFlags.ColorAttachmentWriteBit),
            _ => throw new InvalidOperationException(
                $"Unsupported image layout transition: {oldLayout} -> {newLayout}.")
        };

    private static ImageMemoryBarrier CreateBarrier(
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags srcAccess,
        AccessFlags dstAccess) =>
        new()
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
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };
}
