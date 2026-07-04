namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class MediaForgeRenderGraphPlan
{
    public MediaForgeRenderGraphPlan(IReadOnlyList<MediaForgeRenderGraphNode> nodes)
    {
        Nodes = nodes;
    }

    public IReadOnlyList<MediaForgeRenderGraphNode> Nodes { get; }

    public int Count(MediaForgeRenderGraphNodeKind kind) =>
        Nodes.Count(node => node.Kind == kind);
}
