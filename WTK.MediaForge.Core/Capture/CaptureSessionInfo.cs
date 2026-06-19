using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public sealed class CaptureSessionInfo
{
    public required GpuAdapterLuid CaptureAdapterLuid { get; init; }
    public required FrameSize DuplicationTextureSize { get; init; }
    public required string TextureFormat { get; init; }
    public required uint RefreshRateNumerator { get; init; }
    public required uint RefreshRateDenominator { get; init; }
}
