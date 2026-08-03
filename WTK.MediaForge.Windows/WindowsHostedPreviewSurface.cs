using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Windows;

public sealed class WindowsHostedPreviewSurface : HostedPreviewSurface
{
    private nint _windowHandle;

    public WindowsHostedPreviewSurface()
        : base(HostedPreviewSurfaceId.New())
    {
    }

    internal WindowsHostedPreviewSurface(nint windowHandle)
        : this()
    {
        SetNativeWindowHandle(windowHandle);
    }

    internal void SetNativeWindowHandle(nint windowHandle)
    {
        if (windowHandle == 0)
            throw new ArgumentException("Window handle cannot be zero.", nameof(windowHandle));

        _windowHandle = windowHandle;
    }

    protected override RenderOutputTarget CreateRenderOutputTargetCore()
    {
        if (_windowHandle == 0)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"output.{RenderOutputTypes.PreviewWindow.Value}",
                "Hosted preview surface has no native Windows surface bound by the platform adapter.");
        }

        return new WindowsHostedPreviewRenderOutputTarget(_windowHandle);
    }

    protected override ValueTask RebindCoreAsync(
        HostedPreviewRebindRequest request,
        CancellationToken cancellationToken)
    {
        if (_windowHandle == 0)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"output.{RenderOutputTypes.PreviewWindow.Value}",
                "Hosted preview native-surface rebind requires a Windows adapter-bound surface.");
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class WindowsHostedPreviewRenderOutputTarget(nint windowHandle) : RenderOutputTarget
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;

    public nint WindowHandle { get; } = windowHandle;
}
