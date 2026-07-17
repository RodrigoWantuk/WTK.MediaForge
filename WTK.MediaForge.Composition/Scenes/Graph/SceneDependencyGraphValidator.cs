using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal static class SceneDependencyGraphValidator
{
    public static ProjectValidationResult Validate(SceneDependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var issues = new List<ValidationIssue>();
        foreach (var canvasId in graph.CanvasIds)
        {
            foreach (var nested in graph.GetNestedCanvases(canvasId))
            {
                if (!graph.Contains(nested))
                {
                    issues.Add(ValidationIssue.Error(
                        "scene.graph.canvas_missing",
                        $"Canvas {canvasId} references missing nested canvas {nested}."));
                }
            }

            if (TryFindCycle(canvasId, graph, out var cycle))
            {
                issues.Add(ValidationIssue.Error(
                    "scene.graph.cycle",
                    $"Scene dependency cycle detected: {string.Join(" -> ", cycle)}."));
                break;
            }

            var depth = MeasureDepth(canvasId, graph, []);
            if (depth > CanvasGraphLimits.MaxNestedCanvasDepth)
            {
                issues.Add(ValidationIssue.Error(
                    "scene.graph.depth",
                    $"Scene dependency depth from {canvasId} exceeds max {CanvasGraphLimits.MaxNestedCanvasDepth}."));
            }
        }

        return new ProjectValidationResult(issues);
    }

    private static bool TryFindCycle(
        CanvasId start,
        SceneDependencyGraph graph,
        out IReadOnlyList<CanvasId> cycle)
    {
        var visiting = new HashSet<CanvasId>();
        var visited = new HashSet<CanvasId>();
        var stack = new List<CanvasId>();
        return TryFindCycleDfs(start, graph, visiting, visited, stack, out cycle);
    }

    private static bool TryFindCycleDfs(
        CanvasId current,
        SceneDependencyGraph graph,
        HashSet<CanvasId> visiting,
        HashSet<CanvasId> visited,
        List<CanvasId> stack,
        out IReadOnlyList<CanvasId> cycle)
    {
        if (visiting.Contains(current))
        {
            var cycleStart = stack.IndexOf(current);
            cycle = stack.Skip(cycleStart).Append(current).ToArray();
            return true;
        }

        if (!visited.Add(current))
        {
            cycle = Array.Empty<CanvasId>();
            return false;
        }

        visiting.Add(current);
        stack.Add(current);

        foreach (var nested in graph.GetNestedCanvases(current))
        {
            if (TryFindCycleDfs(nested, graph, visiting, visited, stack, out cycle))
                return true;
        }

        stack.RemoveAt(stack.Count - 1);
        visiting.Remove(current);
        cycle = Array.Empty<CanvasId>();
        return false;
    }

    private static int MeasureDepth(CanvasId canvasId, SceneDependencyGraph graph, HashSet<CanvasId> visiting)
    {
        if (!visiting.Add(canvasId))
            return 0;

        var children = graph.GetNestedCanvases(canvasId);
        if (children.Count == 0)
        {
            visiting.Remove(canvasId);
            return 0;
        }

        var depth = children.Max(child => MeasureDepth(child, graph, visiting)) + 1;
        visiting.Remove(canvasId);
        return depth;
    }
}
