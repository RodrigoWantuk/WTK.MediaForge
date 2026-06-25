using System.Collections.Concurrent;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Win32 preview panels should publish client size from the UI thread during resize.
/// Vulkan surface capabilities can lag behind HWND layout on WinForms controls.
/// </summary>
internal static class PreviewPanelClientSizeTracker
{
    private static readonly ConcurrentDictionary<nint, PanelClientSize> Sizes = new();

    internal static void NotifyClientSize(nint panelHandle, uint width, uint height)
    {
        if (panelHandle == 0)
            return;

        if (width == 0 || height == 0)
            return;

        Sizes[panelHandle] = new PanelClientSize(width, height);
    }

    internal static void RemovePanel(nint panelHandle)
    {
        if (panelHandle == 0)
            return;

        Sizes.TryRemove(panelHandle, out _);
    }

    internal static bool TryGetClientSize(nint panelHandle, out uint width, out uint height)
    {
        width = 0;
        height = 0;

        if (panelHandle == 0)
            return false;

        if (!Sizes.TryGetValue(panelHandle, out var size))
            return false;

        width = size.Width;
        height = size.Height;
        return true;
    }

    private readonly record struct PanelClientSize(uint Width, uint Height);
}
