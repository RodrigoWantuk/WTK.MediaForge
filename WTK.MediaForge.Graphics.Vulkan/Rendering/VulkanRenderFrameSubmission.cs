using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanRenderFrameSubmission : IRenderFrameSubmission, IDisposable
{
    private const int DefaultDisposeWaitSeconds = 5;

    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly List<VulkanExternalTextureLease> _textureLeases;
    private RenderFrameSnapshot? _snapshot;
    private int _resourcesDisposed;

    public VulkanRenderFrameSubmission(
        VulkanHeadlessDevice deviceContext,
        RenderFrameSnapshot snapshot,
        CommandBuffer commandBuffer,
        Fence fence,
        IReadOnlyList<VulkanExternalTextureLease> textureLeases,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        CommandBuffer = commandBuffer;
        Fence = fence;
        _textureLeases = textureLeases.ToList();
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

        foreach (var lease in _textureLeases)
            lease.Dispose();

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
            {
                var fenceTimeout = new TimeoutException("Timed out waiting for Vulkan submission fence to complete.");
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "vulkan.fence_wait_timeout",
                    fenceTimeout.Message,
                    nameof(VulkanRenderFrameSubmission),
                    fenceTimeout);
                throw fenceTimeout;
            }

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
