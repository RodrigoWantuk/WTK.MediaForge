using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;

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
            RenderGraphNodeKind.Primitive => new PrimitiveRenderGraphNode
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
            RenderGraphNodeKind.Transition => new OutputTransitionRenderGraphNode
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
            MediaForgeRenderGraphNodeKind.LayerEffectChain => RenderGraphNodeKind.Transform,
            MediaForgeRenderGraphNodeKind.SourceLayer => RenderGraphNodeKind.Transform,
            MediaForgeRenderGraphNodeKind.PrimitiveLayer => RenderGraphNodeKind.Primitive,
            MediaForgeRenderGraphNodeKind.CanvasRender => RenderGraphNodeKind.Blend,
            MediaForgeRenderGraphNodeKind.CanvasEffectChain => RenderGraphNodeKind.Transform,
            MediaForgeRenderGraphNodeKind.OutputTransition => RenderGraphNodeKind.Transition,
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
                    WasSkipped = true,
                    FailureReason = "Source layer is hidden."
                };
            }
        }

        var requiredSourceId = ExtractSourceId(PlanNode.Key);
        if (requiredSourceId is null)
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = "Source node key does not contain a source id."
            };
        }

        if (!context.SourceFrames.TryGetValue(requiredSourceId.Value, out var frame))
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = "Source frame is unavailable for this graph execution."
            };
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            SourceFrame = frame
        };
    }

    private bool IsSourceHidden(SourceId sourceId)
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

    private static SourceId? ExtractSourceId(string key)
    {
        const string prefix = "source:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var sourceIdText = key[prefix.Length..].Split(':', 2)[0];
        return Guid.TryParse(sourceIdText, out var value)
            ? SourceId.From(value)
            : null;
    }
}

internal sealed class TransformRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        if (!RenderGraphNodeResourceFlow.TryGetDependencyResources(
                this,
                context,
                out var sourceFrame,
                out var outputTexture,
                out var producedPrimitive,
                out var failureReason))
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = failureReason
            };
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            SourceFrame = sourceFrame,
            OutputTexture = outputTexture,
            ProducedPrimitive = producedPrimitive
        };
    }
}

internal sealed class BlendRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        if (Dependencies.Count == 0)
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = "Canvas render node has no renderable dependencies."
            };
        }

        if (!RenderGraphNodeResourceFlow.TryGetAnyDependencyResource(
                this,
                context,
                out var sourceFrame,
                out var outputTexture,
                out var producedPrimitive,
                out var failureReason))
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = failureReason
            };
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            SourceFrame = sourceFrame,
            OutputTexture = outputTexture,
            ProducedPrimitive = producedPrimitive
        };
    }
}

internal sealed class PrimitiveRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            ProducedPrimitive = true
        };
    }
}

internal sealed class OutputRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        if (!RenderGraphNodeResourceFlow.TryGetDependencyResources(
                this,
                context,
                out var sourceFrame,
                out var outputTexture,
                out var producedPrimitive,
                out var failureReason))
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = failureReason
            };
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            SourceFrame = sourceFrame,
            OutputTexture = outputTexture,
            ProducedPrimitive = producedPrimitive
        };
    }
}

internal sealed class OutputTransitionRenderGraphNode : RenderGraphNode
{
    public required MediaForgeRenderGraphNode PlanNode { get; init; }

    public override RenderGraphNodeResult Execute(RenderGraphContext context)
    {
        if (!RenderGraphNodeResourceFlow.TryGetDependencyResources(
                this,
                context,
                out var sourceFrame,
                out var outputTexture,
                out var producedPrimitive,
                out var failureReason))
        {
            return new RenderGraphNodeResult
            {
                NodeKey = Key,
                Kind = Kind,
                WasSkipped = true,
                FailureReason = failureReason
            };
        }

        return new RenderGraphNodeResult
        {
            NodeKey = Key,
            Kind = Kind,
            WasSkipped = false,
            SourceFrame = sourceFrame,
            OutputTexture = outputTexture,
            ProducedPrimitive = producedPrimitive
        };
    }
}

internal static class RenderGraphNodeResourceFlow
{
    public static bool TryGetDependencyResources(
        RenderGraphNode node,
        RenderGraphContext context,
        out GpuFrameReference? sourceFrame,
        out GpuTextureLease? outputTexture,
        out bool producedPrimitive,
        out string? failureReason)
    {
        sourceFrame = null;
        outputTexture = null;
        producedPrimitive = false;
        failureReason = null;

        if (node.Dependencies.Count == 0)
        {
            failureReason = "Node has no dependencies that can produce a renderable resource.";
            return false;
        }

        foreach (var dependency in node.Dependencies)
        {
            if (!context.NodeResults.TryGetValue(dependency, out var result))
            {
                failureReason = $"Dependency '{dependency}' did not execute before node '{node.Key}'.";
                return false;
            }

            if (result.WasSkipped)
            {
                failureReason = result.FailureReason ?? $"Dependency '{dependency}' was skipped.";
                return false;
            }

            if (!result.HasRenderableResource)
            {
                failureReason = $"Dependency '{dependency}' produced no renderable resource.";
                return false;
            }

            sourceFrame ??= result.SourceFrame;
            outputTexture ??= result.OutputTexture;
            producedPrimitive |= result.ProducedPrimitive;
        }

        return sourceFrame.HasValue || outputTexture is not null || producedPrimitive;
    }

    public static bool TryGetAnyDependencyResource(
        RenderGraphNode node,
        RenderGraphContext context,
        out GpuFrameReference? sourceFrame,
        out GpuTextureLease? outputTexture,
        out bool producedPrimitive,
        out string? failureReason)
    {
        sourceFrame = null;
        outputTexture = null;
        producedPrimitive = false;
        failureReason = null;

        if (node.Dependencies.Count == 0)
        {
            failureReason = "Node has no dependencies that can produce a renderable resource.";
            return false;
        }

        foreach (var dependency in node.Dependencies)
        {
            if (!context.NodeResults.TryGetValue(dependency, out var result))
            {
                failureReason = $"Dependency '{dependency}' did not execute before node '{node.Key}'.";
                return false;
            }

            if (result.WasSkipped || !result.HasRenderableResource)
            {
                failureReason ??= result.FailureReason ?? $"Dependency '{dependency}' produced no renderable resource.";
                continue;
            }

            sourceFrame ??= result.SourceFrame;
            outputTexture ??= result.OutputTexture;
            producedPrimitive |= result.ProducedPrimitive;
            return true;
        }

        failureReason ??= "Dependencies produced no renderable resource.";
        return false;
    }
}
