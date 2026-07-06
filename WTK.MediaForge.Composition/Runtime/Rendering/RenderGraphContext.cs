using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderGraphContext
{
    public required FrameExecutionContext FrameContext { get; init; }

    public SceneRuntimeSnapshot? SceneSnapshot { get; init; }

    public Dictionary<string, RenderGraphNodeResult> NodeResults { get; } = new(StringComparer.Ordinal);
}

internal sealed class RenderGraphNodeResult
{
    public required string NodeKey { get; init; }

    public required RenderGraphNodeKind Kind { get; init; }

    public bool WasSkipped { get; init; }
}
