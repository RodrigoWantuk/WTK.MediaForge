namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IPreviewPresentableRenderedOutputSurfaceLease
{
    ValueTask PresentToWin32PanelAsync(nint panelHandle, CancellationToken cancellationToken);
}
