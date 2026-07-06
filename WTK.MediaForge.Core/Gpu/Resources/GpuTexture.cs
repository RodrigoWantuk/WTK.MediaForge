namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuTexture : GpuResource
{
    public GpuTexture(GpuTextureDescriptor descriptor, IGpuPhysicalResource? physical = null)
        : base(GpuResourceKind.Texture)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Physical = physical;
    }

    public GpuTextureDescriptor Descriptor { get; }

    internal IGpuPhysicalResource? Physical { get; set; }

    internal GpuFence? RetirementFence { get; set; }
}
