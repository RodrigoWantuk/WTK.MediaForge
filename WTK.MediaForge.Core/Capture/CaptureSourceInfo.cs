using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public sealed class CaptureSourceInfo
{
    public required uint AdapterIndex { get; init; }
    public required uint OutputIndex { get; init; }
    public required string AdapterName { get; init; }
    public required string OutputName { get; init; }
    public required GpuAdapterLuid AdapterLuid { get; init; }
    public required DesktopRect DesktopRect { get; init; }

    /// <summary>Logical desktop size (after OS rotation).</summary>
    public required FrameSize LogicalSize { get; init; }

    /// <summary>Native duplication texture size when known; otherwise unset.</summary>
    public FrameSize TextureSize { get; init; }

    public DisplayRotation Rotation { get; init; } = DisplayRotation.None;

    /// <summary>Backward-compatible alias for logical size.</summary>
    public FrameSize Size => LogicalSize;

    public override string ToString()
    {
        return $"{OutputName} | logical={LogicalSize} | rot={Rotation} | adapter={AdapterIndex} ({AdapterName})";
    }
}
