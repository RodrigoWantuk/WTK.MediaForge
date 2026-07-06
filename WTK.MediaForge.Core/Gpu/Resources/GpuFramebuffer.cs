namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuFramebuffer : GpuResource
{
    public GpuFramebuffer(GpuTexture colorAttachment)
        : base(GpuResourceKind.Framebuffer)
    {
        ColorAttachment = colorAttachment ?? throw new ArgumentNullException(nameof(colorAttachment));
    }

    public GpuTexture ColorAttachment { get; }
}
