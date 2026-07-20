using Avalonia.Input;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.Views.Preview;

internal static class AvaloniaStudioShortcutMapper
{
    public static bool TryCreate(KeyEventArgs e, out StudioShortcutGesture gesture)
    {
        var key = MapKey(e.Key);
        if (key == StudioShortcutKey.None)
        {
            gesture = default;
            return false;
        }

        gesture = new StudioShortcutGesture(
            key,
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        return true;
    }

    private static StudioShortcutKey MapKey(Key key)
    {
        return key.ToString() switch
        {
            "Z" => StudioShortcutKey.Z,
            "Y" => StudioShortcutKey.Y,
            "S" => StudioShortcutKey.S,
            "O" => StudioShortcutKey.O,
            "N" => StudioShortcutKey.N,
            "D0" or "NumPad0" => StudioShortcutKey.D0,
            "D1" or "NumPad1" => StudioShortcutKey.D1,
            "Add" or "Plus" or "OemPlus" => StudioShortcutKey.Plus,
            "Subtract" or "Minus" or "OemMinus" => StudioShortcutKey.Minus,
            _ => StudioShortcutKey.None
        };
    }
}
