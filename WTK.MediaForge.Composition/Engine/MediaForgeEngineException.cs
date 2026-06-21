namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngineException : Exception
{
    public MediaForgeEngineException(string message, MediaForgeEngineState engineState)
        : base(message)
    {
        EngineState = engineState;
    }

    public MediaForgeEngineException(
        string message,
        MediaForgeEngineState engineState,
        Exception innerException)
        : base(message, innerException)
    {
        EngineState = engineState;
    }

    public MediaForgeEngineState EngineState { get; }
}
