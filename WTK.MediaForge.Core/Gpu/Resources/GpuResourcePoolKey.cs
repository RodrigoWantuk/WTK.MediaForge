namespace WTK.MediaForge.Core.Gpu.Resources;

internal readonly record struct GpuResourcePoolKey(
    GpuTextureUsage Usage,
    int Width,
    int Height,
    string Format)
{
    public static GpuResourcePoolKey From(GpuTextureDescriptor descriptor) =>
        new(descriptor.Usage, descriptor.Width, descriptor.Height, descriptor.Format);
}
