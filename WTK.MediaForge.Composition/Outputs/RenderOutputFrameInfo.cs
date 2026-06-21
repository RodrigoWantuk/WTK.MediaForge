using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class RenderOutputFrameInfo
{
    internal RenderOutputFrameInfo(
        RenderOutputId outputId,
        RenderOutputSinkId sinkId,
        long frameNumber,
        TimeSpan timestamp,
        FrameSize size,
        RenderPixelFormat format,
        RenderBackendKind backendKind)
    {
        OutputId = outputId;
        SinkId = sinkId;
        FrameNumber = frameNumber;
        Timestamp = timestamp;
        Size = size;
        Format = format;
        BackendKind = backendKind;
    }

    public RenderOutputId OutputId { get; }

    public RenderOutputSinkId SinkId { get; }

    public long FrameNumber { get; }

    public TimeSpan Timestamp { get; }

    public FrameSize Size { get; }

    public RenderPixelFormat Format { get; }

    public RenderBackendKind BackendKind { get; }
}
