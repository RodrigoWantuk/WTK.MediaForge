namespace WTK.MediaForge.Composition.Engine;

public enum MediaForgeEngineState
{
    Idle = 0,
    Loaded = 1,
    Starting = 2,
    Running = 3,
    Stopping = 4,
    Failed = 5,
    Disposed = 6
}
