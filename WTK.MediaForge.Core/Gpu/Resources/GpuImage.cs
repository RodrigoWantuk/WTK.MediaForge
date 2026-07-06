namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuImage : GpuResource
{
    public GpuImage(GpuTexture texture)
        : base(GpuResourceKind.Image)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
    }

    public GpuTexture Texture { get; }
}
