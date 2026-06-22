using Silk.NET.Vulkan;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static unsafe class VulkanOffscreenReadback
{
    private static readonly TimeSpan ReadbackTimeout = TimeSpan.FromSeconds(5);
    private const ulong ReadbackPollNanoseconds = 50_000_000;

    private unsafe delegate T MappedRead<T>(byte* pixels, ulong bufferSize);

    public static VulkanReadbackPixel ReadPixel(VulkanOffscreenRenderTarget target, uint x, uint y)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (x >= target.Size.Width)
            throw new ArgumentOutOfRangeException(nameof(x));

        if (y >= target.Size.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        return ReadMapped(
            target,
            CancellationToken.None,
            (pixels, _) =>
            {
                var pixelOffset = checked(((ulong)y * target.Size.Width + x) * 4);
                var bytes = pixels + pixelOffset;
                return new VulkanReadbackPixel(bytes[0], bytes[1], bytes[2], bytes[3]);
            });
    }

    public static VulkanReadbackFrame ReadFrame(
        VulkanOffscreenRenderTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var strideBytes = checked((int)target.Size.Width * 4);
        var length = checked(strideBytes * (int)target.Size.Height);
        var pixels = ReadMapped(
            target,
            cancellationToken,
            (mapped, bufferSize) =>
            {
                if ((ulong)length > bufferSize)
                    throw new InvalidOperationException("Mapped Vulkan readback buffer is smaller than expected.");

                var copy = new byte[length];
                new ReadOnlySpan<byte>(mapped, length).CopyTo(copy);
                return copy;
            });

        return new VulkanReadbackFrame(pixels, strideBytes);
    }

    private static T ReadMapped<T>(
        VulkanOffscreenRenderTarget target,
        CancellationToken cancellationToken,
        MappedRead<T> read)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deviceContext = target.DeviceContext;
        var vk = deviceContext.Vk;
        var device = deviceContext.Device;
        var bufferSize = checked((ulong)target.Size.Width * target.Size.Height * 4);

        lock (deviceContext.CommandQueueGate)
        {
            Silk.NET.Vulkan.Buffer stagingBuffer = default;
            DeviceMemory stagingMemory = default;
            CommandBuffer commandBuffer = default;
            Fence fence = default;
            void* mapped = null;

            try
            {
                CreateStagingBuffer(deviceContext, bufferSize, out stagingBuffer, out stagingMemory);
                commandBuffer = BeginCommandBuffer(deviceContext);

                var originalLayout = target.CurrentLayout;
                if (originalLayout != ImageLayout.TransferSrcOptimal)
                {
                    VulkanImageLayoutTransition.Transition(
                        vk,
                        commandBuffer,
                        target.Image,
                        originalLayout,
                        ImageLayout.TransferSrcOptimal);
                }

                var region = new BufferImageCopy
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    ImageOffset = default,
                    ImageExtent = new Extent3D(target.Size.Width, target.Size.Height, 1)
                };

                vk.CmdCopyImageToBuffer(
                    commandBuffer,
                    target.Image,
                    ImageLayout.TransferSrcOptimal,
                    stagingBuffer,
                    1,
                    &region);

                if (originalLayout != ImageLayout.TransferSrcOptimal)
                {
                    VulkanImageLayoutTransition.Transition(
                        vk,
                        commandBuffer,
                        target.Image,
                        ImageLayout.TransferSrcOptimal,
                        originalLayout);
                }

                if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
                    throw new InvalidOperationException("vkEndCommandBuffer failed for offscreen readback.");

                fence = CreateFence(deviceContext);
                SubmitAndWait(deviceContext, commandBuffer, fence, cancellationToken);
                target.CurrentLayout = originalLayout;

                cancellationToken.ThrowIfCancellationRequested();

                if (vk.MapMemory(device, stagingMemory, 0, bufferSize, 0, &mapped) != Result.Success)
                    throw new InvalidOperationException("vkMapMemory failed for offscreen readback.");

                return read((byte*)mapped, bufferSize);
            }
            finally
            {
                if (mapped is not null)
                    vk.UnmapMemory(device, stagingMemory);

                if (fence.Handle != 0)
                    vk.DestroyFence(device, fence, null);

                if (commandBuffer.Handle != 0)
                {
                    var localCommandBuffer = commandBuffer;
                    vk.FreeCommandBuffers(device, deviceContext.CommandPool, 1, &localCommandBuffer);
                }

                if (stagingBuffer.Handle != 0)
                    vk.DestroyBuffer(device, stagingBuffer, null);

                if (stagingMemory.Handle != 0)
                    vk.FreeMemory(device, stagingMemory, null);
            }
        }
    }

    private static void CreateStagingBuffer(
        VulkanHeadlessDevice deviceContext,
        ulong size,
        out Silk.NET.Vulkan.Buffer buffer,
        out DeviceMemory memory)
    {
        var vk = deviceContext.Vk;
        var device = deviceContext.Device;

        buffer = default;
        memory = default;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, &bufferInfo, null, out buffer) != Result.Success)
            throw new InvalidOperationException("vkCreateBuffer failed for offscreen readback.");

        vk.GetBufferMemoryRequirements(device, buffer, out var requirements);

        var allocationInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = deviceContext.FindMemoryType(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        if (vk.AllocateMemory(device, &allocationInfo, null, out memory) != Result.Success)
            throw new InvalidOperationException("vkAllocateMemory failed for offscreen readback.");

        if (vk.BindBufferMemory(device, buffer, memory, 0) != Result.Success)
            throw new InvalidOperationException("vkBindBufferMemory failed for offscreen readback.");
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
            throw new InvalidOperationException("vkAllocateCommandBuffers failed for offscreen readback.");
        }

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (deviceContext.Vk.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
            throw new InvalidOperationException("vkBeginCommandBuffer failed for offscreen readback.");

        return commandBuffer;
    }

    private static Fence CreateFence(VulkanHeadlessDevice deviceContext)
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo
        };

        if (deviceContext.Vk.CreateFence(deviceContext.Device, &fenceInfo, null, out var fence) != Result.Success)
            throw new InvalidOperationException("vkCreateFence failed for offscreen readback.");

        return fence;
    }

    private static void SubmitAndWait(
        VulkanHeadlessDevice deviceContext,
        CommandBuffer commandBuffer,
        Fence fence,
        CancellationToken cancellationToken)
    {
        var commandBuffers = stackalloc CommandBuffer[1];
        commandBuffers[0] = commandBuffer;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers
        };

        if (deviceContext.Vk.QueueSubmit(deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
            throw new InvalidOperationException("vkQueueSubmit failed for offscreen readback.");

        var deadline = Environment.TickCount64 + (long)ReadbackTimeout.TotalMilliseconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
                throw new TimeoutException("Timed out waiting for offscreen readback.");

            var waitNanoseconds = (ulong)Math.Min(
                checked(remainingMs * 1_000_000L),
                (long)ReadbackPollNanoseconds);
            var result = deviceContext.Vk.WaitForFences(
                deviceContext.Device,
                1,
                &fence,
                true,
                waitNanoseconds);

            if (result == Result.Success)
                return;

            if (result != Result.Timeout)
                throw new InvalidOperationException($"vkWaitForFences failed for offscreen readback: {result}.");
        }
    }
}
