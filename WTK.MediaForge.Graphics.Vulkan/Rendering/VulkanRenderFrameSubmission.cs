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

        WaitForCompletionIfNeeded();

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

    private void WaitForCompletionIfNeeded()
    {
        if (Fence.Handle == 0 || IsCompleted)
            return;

        var fence = Fence;
        var result = _deviceContext.Vk.WaitForFences(
            _deviceContext.Device,
            1,
            in fence,
            true,
            FenceWaitTimeoutNs);

        if (result is not (Result.Success or Result.Timeout))
            throw new InvalidOperationException($"vkWaitForFences failed during submission dispose: {result}");
    }
}
