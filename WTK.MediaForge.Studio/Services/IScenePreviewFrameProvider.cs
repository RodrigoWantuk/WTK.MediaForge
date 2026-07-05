namespace WTK.MediaForge.Studio.Services;

public interface IScenePreviewFrameProvider
{
    bool HasFrame { get; }

    object? CurrentFrame { get; }
}
