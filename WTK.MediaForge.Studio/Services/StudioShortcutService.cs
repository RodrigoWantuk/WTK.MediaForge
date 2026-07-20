namespace WTK.MediaForge.Studio.Services;

public enum StudioShortcutKey
{
    None,
    Z,
    Y,
    S,
    O,
    N,
    D0,
    D1,
    Plus,
    Minus
}

public enum StudioShortcutAction
{
    None,
    Undo,
    Redo,
    SaveProject,
    OpenProject,
    NewProject,
    FitCanvas,
    ActualSize,
    ZoomIn,
    ZoomOut
}

public readonly record struct StudioShortcutGesture(
    StudioShortcutKey Key,
    bool Control,
    bool Shift,
    bool Alt);

public interface IStudioShortcutService
{
    StudioShortcutAction Resolve(StudioShortcutGesture gesture);
}

public sealed class StudioShortcutService : IStudioShortcutService
{
    public StudioShortcutAction Resolve(StudioShortcutGesture gesture)
    {
        if (gesture.Alt)
        {
            return StudioShortcutAction.None;
        }

        if (!gesture.Control)
        {
            return StudioShortcutAction.None;
        }

        return gesture.Key switch
        {
            StudioShortcutKey.Z when gesture.Shift => StudioShortcutAction.Redo,
            StudioShortcutKey.Z => StudioShortcutAction.Undo,
            StudioShortcutKey.Y => StudioShortcutAction.Redo,
            StudioShortcutKey.S => StudioShortcutAction.SaveProject,
            StudioShortcutKey.O => StudioShortcutAction.OpenProject,
            StudioShortcutKey.N => StudioShortcutAction.NewProject,
            StudioShortcutKey.D0 => StudioShortcutAction.FitCanvas,
            StudioShortcutKey.D1 => StudioShortcutAction.ActualSize,
            StudioShortcutKey.Plus => StudioShortcutAction.ZoomIn,
            StudioShortcutKey.Minus => StudioShortcutAction.ZoomOut,
            _ => StudioShortcutAction.None
        };
    }
}
