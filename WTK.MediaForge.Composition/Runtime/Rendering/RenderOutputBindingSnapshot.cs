using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderOutputBindingSnapshot
{
    public RenderOutputId OutputId { get; init; }

    public RenderTargetKind TargetKind { get; init; }

    public nint NativeHandle { get; init; }

    public FrameSize SurfaceSize { get; init; }

    public long BindingVersion { get; init; }
}
