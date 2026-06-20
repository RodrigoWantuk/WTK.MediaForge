using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static unsafe class VulkanCp1OffscreenCompositor
{
    public static List<VulkanOffscreenTargetHandle> Compose(
        Vk vk,
        CommandBuffer commandBuffer,
        RenderFrameSnapshot snapshot,
        IReadOnlyDictionary<RenderOutputId, VulkanOffscreenTargetHandle> offscreenTargets,
        IReadOnlyList<VulkanExternalTextureLease> textureLeases)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(offscreenTargets);
        ArgumentNullException.ThrowIfNull(textureLeases);

        var retained = new List<VulkanOffscreenTargetHandle>();

        var importsByHandle = textureLeases.ToDictionary(
            lease => VulkanExternalTextureKey.From(lease.Import.SourceHandle),
            lease => lease.Import);

        foreach (var output in snapshot.Outputs)
        {
            if (!offscreenTargets.TryGetValue(output.Id, out var targetHandle) || !targetHandle.IsAlive)
                continue;

            var canvas = snapshot.Canvases.FirstOrDefault(c => c.Id == output.CanvasId);
            if (canvas is null)
                continue;

            var sourceLayer = canvas.Objects
                .OfType<RenderSourceLayerDrawObjectSnapshot>()
                .FirstOrDefault(layer => layer.Enabled && layer.BoundFrame?.Handle is D3D11SharedTextureFrameHandle);

            if (sourceLayer?.BoundFrame?.Handle is not D3D11SharedTextureFrameHandle sharedHandle)
                continue;

            if (!importsByHandle.TryGetValue(VulkanExternalTextureKey.From(sharedHandle), out var import))
                continue;

            if (targetHandle.Target is not VulkanOffscreenRenderTarget offscreen)
                continue;

            targetHandle.RetainForSubmission();
            retained.Add(targetHandle);

            ClearOffscreen(vk, commandBuffer, offscreen, output.LetterboxColor);
            BlitSourceToOffscreenFit(
                vk,
                commandBuffer,
                import,
                offscreen,
                sourceLayer.LayoutMode == LayoutMode.Fit ? output.CanvasLayoutMode : sourceLayer.LayoutMode);
        }

        return retained;
    }

    private static void ClearOffscreen(
        Vk vk,
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget offscreen,
        WTK.MediaForge.Core.Color.ColorRgba letterboxColor)
    {
        TransitionToTransferDst(vk, commandBuffer, offscreen.Image, offscreen.CurrentLayout);
        offscreen.CurrentLayout = ImageLayout.TransferDstOptimal;

        var clearColor = new ClearColorValue
        {
            Float32_0 = letterboxColor.R,
            Float32_1 = letterboxColor.G,
            Float32_2 = letterboxColor.B,
            Float32_3 = letterboxColor.A
        };

        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        vk.CmdClearColorImage(commandBuffer, offscreen.Image, ImageLayout.TransferDstOptimal, &clearColor, 1, &range);
    }

    private static void BlitSourceToOffscreenFit(
        Vk vk,
        CommandBuffer commandBuffer,
        VulkanD3D11TextureImport import,
        VulkanOffscreenRenderTarget offscreen,
        LayoutMode layoutMode)
    {
        TransitionImportToTransferSrc(vk, commandBuffer, import);

        var srcWidth = import.Width;
        var srcHeight = import.Height;
        var dstWidth = offscreen.Size.Width;
        var dstHeight = offscreen.Size.Height;

        ComputeFitRect(
            srcWidth,
            srcHeight,
            dstWidth,
            dstHeight,
            layoutMode,
            out var dstX,
            out var dstY,
            out var dstW,
            out var dstH);

        var blitRegion = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.SrcOffsets[1] = new Offset3D((int)srcWidth, (int)srcHeight, 1);
        blitRegion.DstOffsets[0] = new Offset3D((int)dstX, (int)dstY, 0);
        blitRegion.DstOffsets[1] = new Offset3D((int)(dstX + dstW), (int)(dstY + dstH), 1);

        vk.CmdBlitImage(
            commandBuffer,
            import.Image,
            ImageLayout.TransferSrcOptimal,
            offscreen.Image,
            ImageLayout.TransferDstOptimal,
            1,
            &blitRegion,
            Filter.Linear);

        TransitionToGeneral(vk, commandBuffer, offscreen.Image, ImageLayout.TransferDstOptimal);
        offscreen.CurrentLayout = ImageLayout.General;
        import.SetLayout(ImageLayout.General);
    }

    private static void ComputeFitRect(
        uint srcWidth,
        uint srcHeight,
        uint dstWidth,
        uint dstHeight,
        LayoutMode layoutMode,
        out uint dstX,
        out uint dstY,
        out uint dstW,
        out uint dstH)
    {
        if (layoutMode == LayoutMode.Stretch || srcWidth == 0 || srcHeight == 0)
        {
            dstX = 0;
            dstY = 0;
            dstW = dstWidth;
            dstH = dstHeight;
            return;
        }

        var srcAspect = srcWidth / (float)srcHeight;
        var dstAspect = dstWidth / (float)dstHeight;

        if (srcAspect > dstAspect)
        {
            dstW = dstWidth;
            dstH = (uint)Math.Max(1, Math.Round(dstWidth / srcAspect));
            dstX = 0;
            dstY = (dstHeight - dstH) / 2;
        }
        else
        {
            dstH = dstHeight;
            dstW = (uint)Math.Max(1, Math.Round(dstHeight * srcAspect));
            dstY = 0;
            dstX = (dstWidth - dstW) / 2;
        }
    }

    private static void TransitionToTransferDst(Vk vk, CommandBuffer commandBuffer, Image image, ImageLayout oldLayout)
    {
        var barrier = CreateBarrier(image, oldLayout, ImageLayout.TransferDstOptimal, AccessFlags.ShaderReadBit, AccessFlags.TransferWriteBit);
        vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

    private static void TransitionImportToTransferSrc(Vk vk, CommandBuffer commandBuffer, VulkanD3D11TextureImport import)
    {
        if (import.CurrentLayout == ImageLayout.TransferSrcOptimal)
            return;

        var barrier = CreateBarrier(import.Image, import.CurrentLayout, ImageLayout.TransferSrcOptimal, AccessFlags.ShaderReadBit, AccessFlags.TransferReadBit);
        vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
        import.SetLayout(ImageLayout.TransferSrcOptimal);
    }

    private static void TransitionToGeneral(Vk vk, CommandBuffer commandBuffer, Image image, ImageLayout oldLayout)
    {
        var barrier = CreateBarrier(image, oldLayout, ImageLayout.General, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

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
