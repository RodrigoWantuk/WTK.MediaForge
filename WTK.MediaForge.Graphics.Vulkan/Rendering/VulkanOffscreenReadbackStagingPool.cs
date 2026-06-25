using Silk.NET.Vulkan;
using VulkanBuffer = Silk.NET.Vulkan.Buffer;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static unsafe class VulkanOffscreenReadbackStagingPool
{
    private static readonly object Gate = new();
    private static readonly Dictionary<StagingPoolKey, Stack<StagingBufferLease>> Available = [];

    internal static int LiveLeaseCountForTests
    {
        get
        {
            lock (Gate)
                return Available.Values.Sum(static stack => stack.Count);
        }
    }

    public static StagingBufferLease Rent(VulkanHeadlessDevice deviceContext, ulong size)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);

        lock (Gate)
        {
            var key = new StagingPoolKey(deviceContext.Device.Handle, size);
            if (Available.TryGetValue(key, out var stack) && stack.Count > 0)
                return stack.Pop();
        }

        return Create(deviceContext, size);
    }

    public static void Return(VulkanHeadlessDevice deviceContext, StagingBufferLease lease)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(lease);

        lock (Gate)
        {
            var key = new StagingPoolKey(deviceContext.Device.Handle, lease.Size);
            if (!Available.TryGetValue(key, out var stack))
            {
                stack = new Stack<StagingBufferLease>();
                Available[key] = stack;
            }

            stack.Push(lease);
        }
    }

    public static void DisposeAllForDevice(VulkanHeadlessDevice deviceContext)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);

        lock (Gate)
        {
            var keysToRemove = Available.Keys
                .Where(key => key.DeviceHandle == deviceContext.Device.Handle)
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (!Available.TryGetValue(key, out var stack))
                    continue;

                while (stack.Count > 0)
                    stack.Pop().Dispose(deviceContext);

                Available.Remove(key);
            }
        }
    }

    private static StagingBufferLease Create(VulkanHeadlessDevice deviceContext, ulong size)
    {
        var vk = deviceContext.Vk;
        var device = deviceContext.Device;

        VulkanBuffer buffer = default;
        DeviceMemory memory = default;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, in bufferInfo, null, out buffer) != Result.Success)
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

        if (vk.AllocateMemory(device, in allocationInfo, null, out memory) != Result.Success)
            throw new InvalidOperationException("vkAllocateMemory failed for offscreen readback.");

        if (vk.BindBufferMemory(device, buffer, memory, 0) != Result.Success)
            throw new InvalidOperationException("vkBindBufferMemory failed for offscreen readback.");

        return new StagingBufferLease(buffer, memory, size);
    }

    private readonly record struct StagingPoolKey(nint DeviceHandle, ulong Size);
}

internal sealed unsafe class StagingBufferLease
{
    public StagingBufferLease(VulkanBuffer buffer, DeviceMemory memory, ulong size)
    {
        Buffer = buffer;
        Memory = memory;
        Size = size;
    }

    public VulkanBuffer Buffer { get; }

    public DeviceMemory Memory { get; }

    public ulong Size { get; }

    public void Dispose(VulkanHeadlessDevice deviceContext)
    {
        var vk = deviceContext.Vk;
        var device = deviceContext.Device;

        if (Buffer.Handle != 0)
            vk.DestroyBuffer(device, Buffer, null);

        if (Memory.Handle != 0)
            vk.FreeMemory(device, Memory, null);
    }
}
