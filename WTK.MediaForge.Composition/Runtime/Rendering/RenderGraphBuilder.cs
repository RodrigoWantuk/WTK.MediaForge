using WTK.MediaForge.Composition.Runtime.Scene;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal static class RenderGraphBuilder
{
    public static RenderGraph FromPlan(
        MediaForgeRenderGraphPlan plan,
        SceneRuntimeSnapshot? sceneSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var nodes = plan.Nodes
            .Select(planNode => CreateNode(planNode, sceneSnapshot))
            .ToArray();

        return new RenderGraph(nodes);
    }

    private static RenderGraphNode CreateNode(
        MediaForgeRenderGraphNode planNode,
        SceneRuntimeSnapshot? sceneSnapshot)
    {
        var kind = MapKind(planNode.Kind);
        return kind switch
        {
            RenderGraphNodeKind.Source => new SourceRenderGraphNode
            {
                Key = planNode.Key,
                Kind = kind,
                Dependencies = planNode.Dependencies,
                PlanNode = planNode,
                SceneSnapshot = sceneSnapshot
            },
            RenderGraphNodeKind.Transform => new TransformRenderGraphNode
            {
                Key = planNode.Key,
                Kind = kind,
                Dependencies = planNode.Dependencies,
                PlanNode = planNode
            },
            RenderGraphNodeKind.Blend => new BlendRenderGraphNode
            {
                Key = planNode.Key,
                Kind = kind,
                Dependencies = planNode.Dependencies,
                PlanNode = planNode
            },
            RenderGraphNodeKind.Output => new OutputRenderGraphNode
            {
                Key = planNode.Key,
                Kind = kind,
                Dependencies = planNode.Dependencies,
                PlanNode = planNode
            },
            _ => throw new NotSupportedException($"Unsupported render graph node kind '{kind}'.")
        };
    }

    private static RenderGraphNodeKind MapKind(MediaForgeRenderGraphNodeKind planKind) =>
        planKind switch
        {
            MediaForgeRenderGraphNodeKind.SourceFrame => RenderGraphNodeKind.Source,
            MediaForgeRenderGraphNodeKind.SourceEffectChain => RenderGraphNodeKind.Transform,
            MediaForgeRenderGraphNodeKind.CanvasRender => RenderGraphNodeKind.Blend,
            MediaForgeRenderGraphNodeKind.OutputPass => RenderGraphNodeKind.Output,
            _ => throw new NotSupportedException($"Unsupported planner node kind '{planKind}'.")
        };
}

internal sealed class SourceRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public SceneRuntimeSnapshot? SceneSnapshot { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        if (SceneSnapshot is not null &&
            PlanNode.Kind == MediaForgeRenderGraphNodeKind.SourceFrame &&
            SceneSnapshot.HiddenLayerIds.Count > 0 &&
            !PlanNode.Dependencies.Any())
        {
            var sourceId = ExtractSourceId(PlanNode.Key);
            if (sourceId is not null && IsSourceHidden(sourceId.Value))
            {
                return new RenderGraphNodeResult
                {
                    NodeKey = Key,
                    Kind = Kind,
                    WasSkipped = true
                };
            }
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false
        };
    }

    private bool IsSourceHidden(Core.Identifiers.SourceId sourceId)
    {
        if (SceneSnapshot is null)
            return false;

        foreach (var layer in SceneSnapshot.Layers.Values)
        {
            if (layer.BoundSourceId == sourceId && !layer.IsVisible)
                return true;
        }

        return false;
    }

    private static Core.Identifiers.SourceId? ExtractSourceId(string key)
    {
        const string prefix = "source:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        return Guid.TryParse(key[prefix.Length..], out var value)
            ? Core.Identifiers.SourceId.From(value)
            : null;
    }
}

internal sealed class TransformRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context) =>
        new()
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = Dependencies.Any(dependency =>
                context.NodeResults.TryGetValue(dependency, out var result) &&
                result.WasSkipped)
        };
}

internal sealed class BlendRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context) =>
        new()
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = Dependencies.Count > 0 &&
                         Dependencies.All(dependency =>
                             context.NodeResults.TryGetValue(dependency, out var result) &&
                             result.WasSkipped)
        };
}

internal sealed class OutputRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context) =>
        new()
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = Dependencies.Any(dependency =>
                context.NodeResults.TryGetValue(dependency, out var result) &&
                result.WasSkipped)
        };
}
