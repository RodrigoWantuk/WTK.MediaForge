using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;

namespace WTK.MediaForge.Core.Gpu;

public readonly record struct GpuFrameReference
{
    public SourceId SourceId { get; init; }

    public GpuFrameBackend Backend { get; init; }

    public IGpuFrameHandle? Handle { get; init; }

    public FrameSize TextureSize { get; init; }

    public FrameSize LogicalSize { get; init; }

    public DisplayRotation Rotation { get; init; }

    public long FrameNumber { get; init; }

    public MediaTime Timestamp { get; init; }
}
