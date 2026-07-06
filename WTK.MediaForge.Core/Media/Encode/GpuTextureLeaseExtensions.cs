using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Core.Media.Encode;

public static class GpuTextureLeaseExtensions
{
    public static GpuVideoFrameDescriptor ToGpuVideoFrameDescriptor(this GpuTextureLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return new GpuVideoFrameDescriptor
        {
            Width = lease.Width,
            Height = lease.Height,
            Format = lease.Format,
            TransportKind = MediaTransportKind.GpuSurface
        };
    }
}
