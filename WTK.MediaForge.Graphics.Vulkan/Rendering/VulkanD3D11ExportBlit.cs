using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static unsafe class VulkanD3D11ExportBlit
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static void CopyOffscreenToSharedTexture(
        VulkanHeadlessDevice deviceContext,
        VulkanOffscreenRenderTarget source,
        D3D11SharedTextureFrameHandle destination,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var waitTimeout = timeout ?? DefaultTimeout;
        using var exportImport = VulkanD3D11ExportImport.Import(deviceContext, destination);

        var vk = deviceContext.Vk;
        var device = deviceContext.Device;
        CommandBuffer commandBuffer = default;
        Fence fence = default;

        try
        {
            commandBuffer = BeginCommandBuffer(deviceContext);

            var sourceLayout = source.CurrentLayout;
            if (sourceLayout != ImageLayout.TransferSrcOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    source.Image,
                    sourceLayout,
                    ImageLayout.TransferSrcOptimal);
            }

            var destLayout = exportImport.CurrentLayout;
            if (destLayout != ImageLayout.TransferDstOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    exportImport.Image,
                    destLayout,
                    ImageLayout.TransferDstOptimal);
            }

            var blitRegion = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1
                }
            };
            blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.SrcOffsets[1] = new Offset3D((int)source.Size.Width, (int)source.Size.Height, 1);
            blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.DstOffsets[1] = new Offset3D(
                (int)destination.TextureSize.Width,
                (int)destination.TextureSize.Height,
                1);

            vk.CmdBlitImage(
                commandBuffer,
                source.Image,
                ImageLayout.TransferSrcOptimal,
                exportImport.Image,
                ImageLayout.TransferDstOptimal,
                1,
                in blitRegion,
                Filter.Linear);

            if (sourceLayout != ImageLayout.TransferSrcOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    source.Image,
                    ImageLayout.TransferSrcOptimal,
                    sourceLayout);
            }

            exportImport.SetLayout(ImageLayout.General);
            source.CurrentLayout = sourceLayout;

            if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
                throw new InvalidOperationException("vkEndCommandBuffer failed for D3D11 export blit.");

            fence = CreateFence(deviceContext);
            SubmitWithKeyedMutex(deviceContext, commandBuffer, exportImport, fence);
            WaitFence(deviceContext, fence, waitTimeout, cancellationToken);

            destination.NotifyVulkanReleasedToProducer();
        }
        finally
        {
            if (fence.Handle != 0)
                vk.DestroyFence(device, fence, null);

            if (commandBuffer.Handle != 0)
            {
                var localCommandBuffer = commandBuffer;
                vk.FreeCommandBuffers(device, deviceContext.CommandPool, 1, &localCommandBuffer);
            }
        }
    }

    public static void ClearOffscreenColor(
        VulkanOffscreenRenderTarget target,
        float r,
        float g,
        float b,
        float a,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var deviceContext = target.DeviceContext;
        var waitTimeout = timeout ?? DefaultTimeout;
        var vk = deviceContext.Vk;
        var device = deviceContext.Device;
        CommandBuffer commandBuffer = default;
        Fence fence = default;

        try
        {
            commandBuffer = BeginCommandBuffer(deviceContext);

            var originalLayout = target.CurrentLayout;
            if (originalLayout != ImageLayout.TransferDstOptimal)
            {
                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    target.Image,
                    originalLayout,
                    ImageLayout.TransferDstOptimal);
            }

            var clearColor = new ClearColorValue(r, g, b, a);
            var clearRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            vk.CmdClearColorImage(
                commandBuffer,
                target.Image,
                ImageLayout.TransferDstOptimal,
                &clearColor,
                1,
                &clearRange);

            if (originalLayout != ImageLayout.TransferDstOptimal)
            {
                var restoreLayout = originalLayout == ImageLayout.Undefined
                    ? ImageLayout.ColorAttachmentOptimal
                    : originalLayout;

                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    target.Image,
                    ImageLayout.TransferDstOptimal,
                    restoreLayout);

                target.CurrentLayout = restoreLayout;
            }
            else
            {
                target.CurrentLayout = ImageLayout.TransferDstOptimal;
            }

            if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
                throw new InvalidOperationException("vkEndCommandBuffer failed for offscreen clear.");

            fence = CreateFence(deviceContext);
            SubmitSimple(deviceContext, commandBuffer, fence);
            WaitFence(deviceContext, fence, waitTimeout, cancellationToken);
        }
        finally
        {
            if (fence.Handle != 0)
                vk.DestroyFence(device, fence, null);

            if (commandBuffer.Handle != 0)
            {
                var localCommandBuffer = commandBuffer;
                vk.FreeCommandBuffers(device, deviceContext.CommandPool, 1, &localCommandBuffer);
            }
        }
    }

    private static CommandBuffer BeginCommandBuffer(VulkanHeadlessDevice deviceContext)
    {
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = deviceContext.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (deviceContext.Vk.AllocateCommandBuffers(deviceContext.Device, &allocateInfo, out var commandBuffer) !=
            Result.Success)
        {
            throw new InvalidOperationException("vkAllocateCommandBuffers failed for D3D11 export blit.");
        }

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (deviceContext.Vk.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
            throw new InvalidOperationException("vkBeginCommandBuffer failed for D3D11 export blit.");

        return commandBuffer;
    }

    private static Fence CreateFence(VulkanHeadlessDevice deviceContext)
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo
        };

        if (deviceContext.Vk.CreateFence(deviceContext.Device, &fenceInfo, null, out var fence) != Result.Success)
            throw new InvalidOperationException("vkCreateFence failed for D3D11 export blit.");

        return fence;
    }

    private static void SubmitSimple(VulkanHeadlessDevice deviceContext, CommandBuffer commandBuffer, Fence fence)
    {
        var commandBuffers = stackalloc CommandBuffer[1];
        commandBuffers[0] = commandBuffer;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers
        };

        lock (deviceContext.CommandQueueGate)
        {
            if (deviceContext.Vk.QueueSubmit(deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                throw new InvalidOperationException("vkQueueSubmit failed for D3D11 export blit.");
        }
    }

    private static void SubmitWithKeyedMutex(
        VulkanHeadlessDevice deviceContext,
        CommandBuffer commandBuffer,
        VulkanD3D11ExportImport exportImport,
        Fence fence)
    {
        var vk = deviceContext.Vk;
        var acquireSyncs = new[] { exportImport.Memory };
        var releaseSyncs = new[] { exportImport.Memory };
        var acquireKeys = new[] { exportImport.SourceHandle.ProducerAcquireKey };
        var releaseKeys = new[] { D3D11SharedTextureSyncKeys.Producer };
        var acquireTimeouts = new[] { 1_000_000_000u };

        fixed (DeviceMemory* acquireSyncPtr = acquireSyncs)
        fixed (DeviceMemory* releaseSyncPtr = releaseSyncs)
        fixed (ulong* acquireKeyPtr = acquireKeys)
        fixed (ulong* releaseKeyPtr = releaseKeys)
        fixed (uint* acquireTimeoutPtr = acquireTimeouts)
        {
            var keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
            {
                SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                AcquireCount = 1,
                PAcquireSyncs = acquireSyncPtr,
                PAcquireKeys = acquireKeyPtr,
                PAcquireTimeouts = acquireTimeoutPtr,
                ReleaseCount = 1,
                PReleaseSyncs = releaseSyncPtr,
                PReleaseKeys = releaseKeyPtr
            };

            var commandBuffers = stackalloc CommandBuffer[1];
            commandBuffers[0] = commandBuffer;

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                PNext = &keyedMutexInfo,
                CommandBufferCount = 1,
                PCommandBuffers = commandBuffers
            };

            lock (deviceContext.CommandQueueGate)
            {
                if (vk.QueueSubmit(deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                    throw new InvalidOperationException("vkQueueSubmit failed for keyed mutex export blit.");
            }
        }
    }

    private static void WaitFence(
        VulkanHeadlessDevice deviceContext,
        Fence fence,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var vk = deviceContext.Vk;
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
                throw new TimeoutException("Timed out waiting for D3D11 export blit completion.");

            var result = vk.WaitForFences(
                deviceContext.Device,
                1,
                &fence,
                Vk.True,
                (ulong)Math.Min(remainingMs, int.MaxValue) * 1_000_000);

            if (result == Result.Success)
                return;

            if (result != Result.Timeout)
                throw new InvalidOperationException($"vkWaitForFences failed: {result}");
        }
    }
}

internal sealed unsafe class VulkanD3D11ExportImport : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly SharedWin32Handle _importedSharedHandle;
    private Image _image;
    private DeviceMemory _memory;
    private bool _disposed;

    private VulkanD3D11ExportImport(
        Vk vk,
        Device device,
        Image image,
        DeviceMemory memory,
        SharedWin32Handle importedSharedHandle,
        D3D11SharedTextureFrameHandle sourceHandle)
    {
        _vk = vk;
        _device = device;
        _image = image;
        _memory = memory;
        _importedSharedHandle = importedSharedHandle;
        SourceHandle = sourceHandle;
    }

    public D3D11SharedTextureFrameHandle SourceHandle { get; }

    public Image Image => _image;

    public DeviceMemory Memory => _memory;

    public ImageLayout CurrentLayout { get; private set; } = ImageLayout.Undefined;

    internal void SetLayout(ImageLayout layout) => CurrentLayout = layout;

    public static VulkanD3D11ExportImport Import(
        VulkanHeadlessDevice deviceContext,
        D3D11SharedTextureFrameHandle handle)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.HasSharedHandle)
            throw new InvalidOperationException("D3D11 shared texture handle is missing a shared NT handle.");

        var duplicatedHandle = SharedWin32Handle.DuplicateFrom(handle.SharedHandle);
        var format = D3D11VulkanFormatMap.MapOrThrow(handle.Format);

        try
        {
            return Import(
                deviceContext.Vk,
                deviceContext.Device,
                deviceContext.FindMemoryType,
                duplicatedHandle,
                handle,
                format);
        }
        catch
        {
            duplicatedHandle.Dispose();
            throw;
        }
    }

    private static VulkanD3D11ExportImport Import(
        Vk vk,
        Device device,
        Func<uint, MemoryPropertyFlags, uint> findMemoryType,
        SharedWin32Handle importedSharedHandle,
        D3D11SharedTextureFrameHandle sourceHandle,
        Format format)
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
            Format = format,
            Extent = new Extent3D(sourceHandle.TextureSize.Width, sourceHandle.TextureSize.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        if (vk.CreateImage(device, &imageCreateInfo, null, out Image image) != Result.Success)
            throw new InvalidOperationException("Create export Vulkan image failed.");

        DeviceMemory memory = default;

        try
        {
            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements memoryRequirements);

            var importedHandle = importedSharedHandle.DangerousGetHandleForInterop();

            var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
            {
                SType = StructureType.ImportMemoryWin32HandleInfoKhr,
                HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                Handle = importedHandle
            };

            var dedicatedAllocateInfo = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                PNext = &importMemoryInfo,
                Image = image
            };

            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &dedicatedAllocateInfo,
                AllocationSize = memoryRequirements.Size,
                MemoryTypeIndex = findMemoryType(
                    memoryRequirements.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit)
            };

            if (vk.AllocateMemory(device, &allocateInfo, null, out memory) != Result.Success)
                throw new InvalidOperationException("Allocate export Vulkan memory failed.");

            if (vk.BindImageMemory(device, image, memory, 0) != Result.Success)
                throw new InvalidOperationException("Bind export Vulkan image memory failed.");

            return new VulkanD3D11ExportImport(vk, device, image, memory, importedSharedHandle, sourceHandle);
        }
        catch
        {
            if (memory.Handle != 0)
                vk.FreeMemory(device, memory, null);

            vk.DestroyImage(device, image, null);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_image.Handle != 0)
        {
            _vk.DestroyImage(_device, _image, null);
            _image = default;
        }

        if (_memory.Handle != 0)
        {
            _vk.FreeMemory(_device, _memory, null);
            _memory = default;
        }

        _importedSharedHandle.Dispose();
    }
}
