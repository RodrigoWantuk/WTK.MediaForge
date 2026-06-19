using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public readonly struct CaptureFrameStats
{
    public uint AccumulatedFrames { get; init; }
    public bool ProtectedContentMaskedOut { get; init; }
    public bool RectsCoalesced { get; init; }
    public FrameSize AcquiredTextureSize { get; init; }
    public FrameSize OwnedTextureSize { get; init; }
    public bool TextureSizeMismatch { get; init; }
    public CaptureCenterPixel? CenterPixel { get; init; }
    public bool CenterPixelReadSucceeded { get; init; }
}
