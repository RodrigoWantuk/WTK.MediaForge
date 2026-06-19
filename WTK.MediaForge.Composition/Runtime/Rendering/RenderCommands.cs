using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public abstract class RenderCommand;

public sealed class BindOutputCommand : RenderCommand
{
    public required RenderOutputBindingSnapshot Binding { get; init; }
}

public sealed class UnbindOutputCommand : RenderCommand
{
    public required RenderOutputId OutputId { get; init; }
}

public sealed class ResizeOutputCommand : RenderCommand
{
    public required RenderOutputId OutputId { get; init; }

    public required FrameSize SurfaceSize { get; init; }
}

public sealed class StopRenderThreadCommand : RenderCommand;
