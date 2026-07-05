namespace WTK.MediaForge.Core.Media;

public enum MediaTransportKind
{
    EncodedPacket,
    GpuSurface,
    StaticCpuAsset,
    RawCpuVideoFrameException,
    DebugOnlyCpuReadback
}
