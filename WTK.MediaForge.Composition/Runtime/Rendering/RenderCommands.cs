using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal abstract class RenderCommand;

internal sealed class BindOutputCommand : RenderCommand
{
    public required RenderOutputBindingSnapshot Binding { get; init; }
}

internal sealed class UnbindOutputCommand : RenderCommand
{
    public required RenderOutputId OutputId { get; init; }
}

internal sealed class ResizeOutputCommand : RenderCommand
{
    public required RenderOutputId OutputId { get; init; }

    public required FrameSize SurfaceSize { get; init; }
}

internal sealed class StopRenderThreadCommand : RenderCommand;
