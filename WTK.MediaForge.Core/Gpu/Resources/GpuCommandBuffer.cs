namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuCommandBuffer : GpuResource
{
    public GpuCommandBuffer(GpuFence? completionFence = null)
        : base(GpuResourceKind.CommandBuffer)
    {
        CompletionFence = completionFence;
    }

    internal GpuFence? CompletionFence { get; set; }
}
