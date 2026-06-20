using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.Gpu;

public sealed class D3D11GpuFrameSlot
{
    internal D3D11GpuFrameSlot(int slotIndex, D3D11SharedTextureFrameHandle handle)
    {
        if (slotIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        SlotIndex = slotIndex;
        TextureId = handle.TextureId;
        TextureSize = handle.TextureSize;
        Format = handle.Format;
    }

    public int SlotIndex { get; }

    public GpuTextureId TextureId { get; }

    public FrameSize TextureSize { get; }

    public Format Format { get; }

    public D3D11SharedTextureFrameHandle Handle { get; }

    public ulong ProducerAcquireKey => Handle.ProducerAcquireKey;

    public bool IsRetired => Handle.IsRetired;

    public void MarkRetired() => Handle.MarkRetired();

    public void MarkCaptureReleasedToConsumer() => Handle.NotifyCaptureReleasedToConsumer();

    public void MarkConsumerReleasedToProducer() => Handle.NotifyVulkanReleasedToProducer();
}
