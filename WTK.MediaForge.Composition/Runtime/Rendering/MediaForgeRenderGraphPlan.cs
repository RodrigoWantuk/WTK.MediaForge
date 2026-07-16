namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class MediaForgeRenderGraphPlan
{
    private PhysicalRenderGraphPlan? _physicalPlan;

    public MediaForgeRenderGraphPlan(IReadOnlyList<MediaForgeRenderGraphNode> nodes)
    {
        Nodes = nodes;
    }

    public IReadOnlyList<MediaForgeRenderGraphNode> Nodes { get; }

    public PhysicalRenderGraphPlan PhysicalPlan =>
        _physicalPlan ??= PhysicalRenderGraphPlanner.Create(this);

    public int Count(MediaForgeRenderGraphNodeKind kind) =>
        Nodes.Count(node => node.Kind == kind);
}
