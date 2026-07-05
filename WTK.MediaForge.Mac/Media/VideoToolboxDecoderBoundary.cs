namespace WTK.MediaForge.Mac.Media;

public interface IVideoToolboxDecoderBoundary
{
    bool IsAvailable { get; }

    string Codec { get; }
}
