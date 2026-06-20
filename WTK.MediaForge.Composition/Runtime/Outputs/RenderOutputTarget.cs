using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

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
    public override RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;

    public nint WindowHandle { get; init; }
}
