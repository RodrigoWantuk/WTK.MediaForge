using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanRenderFrameSubmission : IRenderFrameSubmission, IDisposable
{
    private const int DefaultDisposeWaitSeconds = 5;

    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly List<VulkanD3D11TextureImport> _imports;
    private RenderFrameSnapshot? _snapshot;
    private int _resourcesDisposed;

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
            if (Volatile.Read(ref _resourcesDisposed) != 0)
                return true;

            if (Fence.Handle == 0)
                return true;

            var status = _deviceContext.Vk.GetFenceStatus(_deviceContext.Device, Fence);
            return status == Result.Success;
        }
    }

    public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        WaitForFenceSync(timeout, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public void DisposeCompleted()
    {
        if (!IsCompleted)
            throw new InvalidOperationException("Submission is not completed.");

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

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

    public void Dispose()
    {
        WaitForCompletionAsync(TimeSpan.FromSeconds(DefaultDisposeWaitSeconds), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        DisposeCompleted();
    }

    public ValueTask DisposeAsync()
    {
        WaitForCompletionAsync(TimeSpan.FromSeconds(DefaultDisposeWaitSeconds), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        DisposeCompleted();
        return ValueTask.CompletedTask;
    }

    private void WaitForFenceSync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _resourcesDisposed) != 0 || IsCompleted)
            return;

        if (Fence.Handle == 0)
            return;

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_deviceContext.Vk.GetFenceStatus(_deviceContext.Device, Fence) == Result.Success)
                return;

            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
                throw new TimeoutException("Timed out waiting for Vulkan submission fence to complete.");

            var waitNs = (ulong)Math.Min(remainingMs * 1_000_000L, int.MaxValue);
            var fence = Fence;
            var result = _deviceContext.Vk.WaitForFences(
                _deviceContext.Device,
                1,
                in fence,
                true,
                waitNs);

            if (result == Result.Success)
                return;

            if (result != Result.Timeout)
                throw new InvalidOperationException($"vkWaitForFences failed while waiting for completion: {result}");
        }
    }
}
