using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal sealed class VulkanTextAtlasUploader : IDisposable
{
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(5);
    private const ulong UploadPollNanoseconds = 50_000_000;

    private readonly VulkanGpuResourcePool _pool;
    private readonly VulkanHeadlessDevice _device;
    private readonly Dictionary<GpuTextureId, UploadedAtlas> _uploaded = [];
    private bool _disposed;

    public VulkanTextAtlasUploader(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _pool = new VulkanGpuResourcePool(device);
    }

    public UploadedAtlas Upload(FontAtlasAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var expectedLength = checked(asset.Width * asset.Height * 4);
        if (asset.AtlasPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Font atlas pixel buffer length {asset.AtlasPixels.Length} does not match {asset.Width}x{asset.Height} RGBA8.",
                nameof(asset));
        }

        var acquired = _pool.AcquireOffscreenTarget(
            new FrameSize((uint)asset.Width, (uint)asset.Height),
            GpuTextureUsage.Intermediate);

        try
        {
            var target = acquired.Target;
            UploadPixels(asset, target);
            target.CurrentLayout = ImageLayout.ShaderReadOnlyOptimal;

            var uploaded = new UploadedAtlas(acquired.Lease.TextureId, target.ImageView, acquired.Lease);
            _uploaded[uploaded.TextureId] = uploaded;
            return uploaded;
        }
        catch
        {
            acquired.Lease.Dispose();
            throw;
        }
    }

    public bool TryGetImageView(GpuTextureId textureId, out ImageView imageView)
    {
        if (_uploaded.TryGetValue(textureId, out var uploaded))
        {
            imageView = uploaded.ImageView;
            return true;
        }

        imageView = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var uploaded in _uploaded.Values)
            uploaded.Lease.Dispose();

        _uploaded.Clear();
        _pool.Dispose();
    }

    private unsafe void UploadPixels(FontAtlasAsset asset, VulkanOffscreenRenderTarget target)
    {
        var vk = _device.Vk;
        var device = _device.Device;
        var bufferSize = checked((ulong)asset.AtlasPixels.Length);

        Silk.NET.Vulkan.Buffer stagingBuffer = default;
        DeviceMemory stagingMemory = default;
        CommandBuffer commandBuffer = default;
        Fence fence = default;
        void* mapped = null;

        lock (_device.AuxiliaryCommandPoolGate)
        {
            try
            {
                CreateStagingBuffer(bufferSize, out stagingBuffer, out stagingMemory);

                if (vk.MapMemory(device, stagingMemory, 0, bufferSize, 0, &mapped) != Result.Success)
                    throw new InvalidOperationException("vkMapMemory failed for text atlas upload.");

                asset.AtlasPixels.CopyTo(new Span<byte>(mapped, asset.AtlasPixels.Length));
                vk.UnmapMemory(device, stagingMemory);
                mapped = null;

                commandBuffer = BeginCommandBuffer();

                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    target.Image,
                    target.CurrentLayout,
                    ImageLayout.TransferDstOptimal);

                var copyRegion = new BufferImageCopy
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
                    ImageExtent = new Extent3D((uint)asset.Width, (uint)asset.Height, 1)
                };

                vk.CmdCopyBufferToImage(
                    commandBuffer,
                    stagingBuffer,
                    target.Image,
                    ImageLayout.TransferDstOptimal,
                    1,
                    &copyRegion);

                VulkanImageLayoutTransition.Transition(
                    vk,
                    commandBuffer,
                    target.Image,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal);

                if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
                    throw new InvalidOperationException("vkEndCommandBuffer failed for text atlas upload.");

                fence = CreateFence();
                SubmitAndWait(commandBuffer, fence);
            }
            finally
            {
                if (mapped is not null)
                    vk.UnmapMemory(device, stagingMemory);

                if (fence.Handle != 0)
                    vk.DestroyFence(device, fence, null);

                if (commandBuffer.Handle != 0)
                    _device.FreeAuxiliaryCommandBuffer(commandBuffer);

                if (stagingBuffer.Handle != 0)
                    vk.DestroyBuffer(device, stagingBuffer, null);

                if (stagingMemory.Handle != 0)
                    vk.FreeMemory(device, stagingMemory, null);
            }
        }
    }

    private unsafe void CreateStagingBuffer(
        ulong bufferSize,
        out Silk.NET.Vulkan.Buffer buffer,
        out DeviceMemory memory)
    {
        var vk = _device.Vk;
        var device = _device.Device;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, &bufferInfo, null, out buffer) != Result.Success)
            throw new InvalidOperationException("vkCreateBuffer failed for text atlas upload.");

        memory = default;

        try
        {
            vk.GetBufferMemoryRequirements(device, buffer, out var requirements);

            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _device.FindMemoryType(
                    requirements.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
            };

            if (vk.AllocateMemory(device, &allocationInfo, null, out memory) != Result.Success)
                throw new InvalidOperationException("vkAllocateMemory failed for text atlas upload.");

            if (vk.BindBufferMemory(device, buffer, memory, 0) != Result.Success)
                throw new InvalidOperationException("vkBindBufferMemory failed for text atlas upload.");
        }
        catch
        {
            if (memory.Handle != 0)
            {
                vk.FreeMemory(device, memory, null);
                memory = default;
            }

            vk.DestroyBuffer(device, buffer, null);
            buffer = default;
            throw;
        }
    }

    private unsafe CommandBuffer BeginCommandBuffer()
        => _device.AllocateAndBeginAuxiliaryCommandBuffer("text atlas upload");

    private unsafe Fence CreateFence()
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo
        };

        if (_device.Vk.CreateFence(_device.Device, &fenceInfo, null, out var fence) != Result.Success)
            throw new InvalidOperationException("vkCreateFence failed for text atlas upload.");

        return fence;
    }

    private unsafe void SubmitAndWait(CommandBuffer commandBuffer, Fence fence)
    {
        var commandBuffers = stackalloc CommandBuffer[1];
        commandBuffers[0] = commandBuffer;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers
        };

        lock (_device.CommandQueueGate)
        {
            if (_device.Vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                throw new InvalidOperationException("vkQueueSubmit failed for text atlas upload.");
        }

        var deadline = Environment.TickCount64 + (long)UploadTimeout.TotalMilliseconds;

        while (true)
        {
            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
                throw new TimeoutException($"Timed out waiting {UploadTimeout} for text atlas upload.");

            var waitNanoseconds = (ulong)Math.Min(
                checked(remainingMs * 1_000_000L),
                (long)UploadPollNanoseconds);
            var result = _device.Vk.WaitForFences(
                _device.Device,
                1,
                &fence,
                true,
                waitNanoseconds);

            if (result == Result.Success)
                return;

            if (result != Result.Timeout)
                throw new InvalidOperationException($"vkWaitForFences failed for text atlas upload: {result}.");
        }
    }
}

internal sealed class UploadedAtlas
{
    public UploadedAtlas(GpuTextureId textureId, ImageView imageView, GpuTextureLease lease)
    {
        TextureId = textureId;
        ImageView = imageView;
        Lease = lease;
    }

    public GpuTextureId TextureId { get; }

    public ImageView ImageView { get; }

    public GpuTextureLease Lease { get; }
}
