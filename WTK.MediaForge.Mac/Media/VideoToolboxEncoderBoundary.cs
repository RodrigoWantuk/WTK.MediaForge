namespace WTK.MediaForge.Mac.Media;

public interface IVideoToolboxEncoderBoundary
{
    bool IsAvailable { get; }

    string Codec { get; }
}
