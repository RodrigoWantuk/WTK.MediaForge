namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal static class PreviewPanelPresenterLifecycle
{
    private static Func<nint, TimeSpan, CancellationToken, ValueTask>? _removePresentersForPanelAsync;

    internal static void RegisterRemovePresentersForPanel(
        Func<nint, TimeSpan, CancellationToken, ValueTask> removePresentersForPanelAsync) =>
        _removePresentersForPanelAsync = removePresentersForPanelAsync ??
                                         throw new ArgumentNullException(nameof(removePresentersForPanelAsync));

    internal static async ValueTask RemovePresentersForPanelAsync(
        nint panelHandle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (panelHandle == 0)
            return;

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Presenter removal timeout must be positive.");

        if (_removePresentersForPanelAsync is { } remove)
            await remove(panelHandle, timeout, cancellationToken).ConfigureAwait(false);

        PreviewPanelClientSizeTracker.RemovePanel(panelHandle);
    }
}
