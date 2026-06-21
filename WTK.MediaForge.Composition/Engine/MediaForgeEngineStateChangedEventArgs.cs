namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngineStateChangedEventArgs : EventArgs
{
    public MediaForgeEngineStateChangedEventArgs(
        MediaForgeEngineState oldState,
        MediaForgeEngineState newState)
    {
        OldState = oldState;
        NewState = newState;
    }

    public MediaForgeEngineState OldState { get; }

    public MediaForgeEngineState NewState { get; }
}
