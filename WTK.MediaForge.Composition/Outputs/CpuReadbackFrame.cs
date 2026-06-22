using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class CpuReadbackFrame
{
    internal CpuReadbackFrame(
        RenderOutputFrameInfo info,
        int strideBytes,
        byte[] pixels)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));

        if (strideBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));

        ArgumentNullException.ThrowIfNull(pixels);

        var requiredLength = checked(strideBytes * (int)info.Size.Height);
        if (pixels.Length < requiredLength)
        {
            throw new ArgumentException(
                "CPU readback pixel buffer is smaller than the declared frame stride and height.",
                nameof(pixels));
        }

        StrideBytes = strideBytes;
        Pixels = pixels;
    }

    public RenderOutputFrameInfo Info { get; }

    public RenderOutputId OutputId => Info.OutputId;

    public RenderOutputSinkId SinkId => Info.SinkId;

    public long FrameNumber => Info.FrameNumber;

    public TimeSpan Timestamp => Info.Timestamp;

    public FrameSize Size => Info.Size;

    public RenderPixelFormat Format => Info.Format;

    public RenderBackendKind BackendKind => Info.BackendKind;

    public int StrideBytes { get; }

    public ReadOnlyMemory<byte> Pixels { get; }
}
