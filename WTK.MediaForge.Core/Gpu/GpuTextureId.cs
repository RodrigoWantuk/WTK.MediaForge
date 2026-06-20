namespace WTK.MediaForge.Core.Gpu;

public readonly record struct GpuTextureId(Guid Value)
{
    public static GpuTextureId New() => new(Guid.NewGuid());
}
