using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanRenderFrameSubmission : IRenderFrameSubmission
{
    private const ulong FenceWaitTimeoutNs = 5_000_000_000;

    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly List<VulkanD3D11TextureImport> _imports;
    private RenderFrameSnapshot? _snapshot;
    private int _disposed;

    public VulkanRenderFrameSubmission(
        VulkanHeadlessDevice deviceContext,
        RenderFrameSnapshot snapshot,
        CommandBuffer commandBuffer,
        Fence fence,
        IReadOnlyList<VulkanD3D11TextureImport> imports)
    {
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        CommandBuffer = commandBuffer;
        Fence = fence;
        _imports = imports.ToList();
    }

    public CommandBuffer CommandBuffer { get; }

    public Fence Fence { get; }

    public bool IsCompleted
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0)
                return true;

            var status = _deviceContext.Vk.GetFenceStatus(_deviceContext.Device, Fence);
            return status == Result.Success;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (!WaitForCompletionIfNeeded())
        {
            Volatile.Write(ref _disposed, 0);
            throw new TimeoutException("Timed out waiting for Vulkan submission fence before dispose.");
        }

        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;

        if (Fence.Handle != 0)
            vk.DestroyFence(device, Fence, null);

        if (CommandBuffer.Handle != 0)
        {
            var commandBuffer = CommandBuffer;
            vk.FreeCommandBuffers(device, _deviceContext.CommandPool, 1, &commandBuffer);
        }

        foreach (var import in _imports)
        {
            import.SourceHandle.NotifyVulkanReleasedToProducer();
            import.Dispose();
        }

        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }

    private bool WaitForCompletionIfNeeded()
    {
        if (Fence.Handle == 0)
            return true;

        if (_deviceContext.Vk.GetFenceStatus(_deviceContext.Device, Fence) == Result.Success)
            return true;

        var fence = Fence;
        var result = _deviceContext.Vk.WaitForFences(
            _deviceContext.Device,
            1,
            in fence,
            true,
            FenceWaitTimeoutNs);

        if (result == Result.Success)
            return true;

        if (result == Result.Timeout)
            return false;

        throw new InvalidOperationException($"vkWaitForFences failed during submission dispose: {result}");
    }
}
