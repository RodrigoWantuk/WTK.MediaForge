using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public sealed class CaptureSourceInfo
{
    public required uint AdapterIndex { get; init; }
    public required uint OutputIndex { get; init; }
    public required string AdapterName { get; init; }
    public required string OutputName { get; init; }
    public required FrameSize Size { get; init; }

    public override string ToString()
    {
        return $"{OutputName} - {Size} ({AdapterName})";
    }
}