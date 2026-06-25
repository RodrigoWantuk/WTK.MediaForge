namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal static class PreviewPanelPresenterLifecycle
{
    private static Action<nint>? _removePresentersForPanel;

    internal static void RegisterRemovePresentersForPanel(Action<nint> removePresentersForPanel) =>
        _removePresentersForPanel = removePresentersForPanel ??
                                    throw new ArgumentNullException(nameof(removePresentersForPanel));

    internal static void RemovePresentersForPanel(nint panelHandle)
    {
        if (panelHandle == 0)
            return;

        PreviewPanelClientSizeTracker.RemovePanel(panelHandle);
        _removePresentersForPanel?.Invoke(panelHandle);
    }
}
