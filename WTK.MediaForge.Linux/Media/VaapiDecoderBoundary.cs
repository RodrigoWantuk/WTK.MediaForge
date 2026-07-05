namespace WTK.MediaForge.Linux.Media;

public interface IVaapiDecoderBoundary
{
    bool IsAvailable { get; }

    string Codec { get; }
}
