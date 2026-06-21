using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public abstract class RenderOutputTarget
{
    public abstract RenderOutputTypeId TypeId { get; }
}

public sealed class OffscreenRenderOutputTarget : RenderOutputTarget
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.Offscreen;
}

public sealed class WinFormsPreviewRenderOutputTarget : RenderOutputTarget
{
    public WinFormsPreviewRenderOutputTarget(nint windowHandle)
    {
        if (windowHandle == 0)
            throw new ArgumentException("Window handle cannot be zero.", nameof(windowHandle));

        WindowHandle = windowHandle;
    }

    public override RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;

    public nint WindowHandle { get; }
}
