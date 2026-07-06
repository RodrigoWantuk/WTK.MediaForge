namespace WTK.MediaForge.Core.Gpu.Resources;

internal interface IGpuTextureFactory
{
    IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor);
}
