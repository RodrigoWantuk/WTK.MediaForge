namespace WTK.MediaForge.Linux.Media;

public interface IVaapiEncoderBoundary
{
    bool IsAvailable { get; }

    string Codec { get; }
}
