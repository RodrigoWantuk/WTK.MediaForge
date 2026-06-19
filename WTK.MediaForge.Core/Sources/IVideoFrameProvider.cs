using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Core.Sources;

public interface IVideoFrameProvider : IMediaSource
{
    /// <summary>
    /// Fast and non-blocking. Returns false when no frame is ready.
    /// Must not wait on capture, GPU mutex, decoder, network, or I/O.
    /// </summary>
    bool TryAcquireLatestFrame(out GpuFrameLease lease);
}
