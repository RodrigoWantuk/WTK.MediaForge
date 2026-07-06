namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal abstract class RenderGraphNode
{
    public required string Key { get; init; }

    public required RenderGraphNodeKind Kind { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public abstract RenderGraphNodeResult Execute(RenderGraphContext context);
}
