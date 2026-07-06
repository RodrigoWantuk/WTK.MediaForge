namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuCommandBufferLease : IDisposable
{
    private GpuCommandBuffer? _commandBuffer;
    private readonly Action<GpuCommandBuffer> _onRelease;
    private int _disposed;

    internal GpuCommandBufferLease(GpuCommandBuffer commandBuffer, Action<GpuCommandBuffer> onRelease)
    {
        _commandBuffer = commandBuffer ?? throw new ArgumentNullException(nameof(commandBuffer));
        _onRelease = onRelease ?? throw new ArgumentNullException(nameof(onRelease));
        commandBuffer.AddLeaseRef();
    }

    internal GpuCommandBuffer CommandBuffer =>
        _commandBuffer ?? throw new ObjectDisposedException(nameof(GpuCommandBufferLease));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var commandBuffer = Interlocked.Exchange(ref _commandBuffer, null);
        if (commandBuffer is null)
            return;

        _onRelease(commandBuffer);
    }
}
