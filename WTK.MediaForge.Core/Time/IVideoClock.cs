namespace WTK.MediaForge.Core.Time;

public interface IVideoClock
{
    TimeSpan CurrentPresentationTime { get; }

    bool IsPlaying { get; }

    void Play();

    void Pause();

    void Seek(TimeSpan presentationTime);
}

internal sealed class VideoClock : IVideoClock
{
    private TimeSpan _presentationTime;
    private bool _playing;

    public TimeSpan CurrentPresentationTime => _presentationTime;

    public bool IsPlaying => _playing;

    public void Play() => _playing = true;

    public void Pause() => _playing = false;

    public void Seek(TimeSpan presentationTime) =>
        _presentationTime = presentationTime < TimeSpan.Zero ? TimeSpan.Zero : presentationTime;

    internal void Advance(TimeSpan delta)
    {
        if (!_playing)
            return;

        _presentationTime += delta;
    }
}
