using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation;

internal static class CanvasGraphValidator
{
    public static IEnumerable<ValidationIssue> Validate(MediaForgeProject project)
    {
        var adjacency = BuildAdjacency(project);
        var canvasById = new Dictionary<CanvasId, MediaForgeCanvas>();
        foreach (var canvas in project.Canvases)
            canvasById.TryAdd(canvas.Id, canvas);

        foreach (var canvas in project.Canvases)
        {
            if (adjacency[canvas.Id].Contains(canvas.Id))
            {
                yield return ValidationIssue.Error(
                    "canvas.nested.self",
                    $"Canvas '{canvas.Name}' ({canvas.Id}) cannot reference itself.");
            }
        }

        foreach (var canvas in project.Canvases)
        {
            if (TryFindCycle(canvas.Id, adjacency, out var cyclePath))
            {
                yield return ValidationIssue.Error(
                    "canvas.nested.cycle",
                    $"Canvas nesting cycle detected: {FormatCycle(cyclePath, canvasById)}.");
                break;
            }
        }

        foreach (var canvas in project.Canvases)
        {
            var maxDepth = MeasureMaxNestedDepth(canvas.Id, adjacency);
            if (maxDepth > CanvasGraphLimits.MaxNestedCanvasDepth)
            {
                yield return ValidationIssue.Error(
                    "canvas.nested.depth",
                    $"Canvas '{canvas.Name}' exceeds max nesting depth {CanvasGraphLimits.MaxNestedCanvasDepth} (found {maxDepth}).");
            }
        }
    }

    private static Dictionary<CanvasId, HashSet<CanvasId>> BuildAdjacency(MediaForgeProject project)
    {
        var adjacency = new Dictionary<CanvasId, HashSet<CanvasId>>();

        foreach (var canvas in project.Canvases)
        {
            if (!adjacency.TryGetValue(canvas.Id, out var edges))
            {
                edges = [];
                adjacency[canvas.Id] = edges;
            }

            foreach (var nestedId in canvas.Objects.OfType<CanvasDrawObject>().Select(o => o.NestedCanvasId))
                edges.Add(nestedId);
        }

        return adjacency;
    }

    private static bool TryFindCycle(
        CanvasId start,
        IReadOnlyDictionary<CanvasId, HashSet<CanvasId>> adjacency,
        out List<CanvasId> cyclePath)
    {
        var visiting = new HashSet<CanvasId>();
        var visited = new HashSet<CanvasId>();
        var stack = new List<CanvasId>();

        return TryFindCycleDfs(start, adjacency, visiting, visited, stack, out cyclePath);
    }

    private static bool TryFindCycleDfs(
        CanvasId current,
        IReadOnlyDictionary<CanvasId, HashSet<CanvasId>> adjacency,
        HashSet<CanvasId> visiting,
        HashSet<CanvasId> visited,
        List<CanvasId> stack,
        out List<CanvasId> cyclePath)
    {
        if (visiting.Contains(current))
        {
            var cycleStart = stack.IndexOf(current);
            cyclePath = stack.Skip(cycleStart).Append(current).ToList();
            return true;
        }

        if (!visited.Add(current))
        {
            cyclePath = [];
            return false;
        }

        visiting.Add(current);
        stack.Add(current);

        if (adjacency.TryGetValue(current, out var children))
        {
            foreach (var child in children)
            {
                if (TryFindCycleDfs(child, adjacency, visiting, visited, stack, out cyclePath))
                    return true;
            }
        }

        stack.RemoveAt(stack.Count - 1);
        visiting.Remove(current);
        cyclePath = [];
        return false;
    }

    private static int MeasureMaxNestedDepth(
        CanvasId start,
        IReadOnlyDictionary<CanvasId, HashSet<CanvasId>> adjacency)
    {
        var visiting = new HashSet<CanvasId>();
        return MeasureMaxNestedDepthDfs(start, adjacency, visiting);
    }

    private static int MeasureMaxNestedDepthDfs(
        CanvasId current,
        IReadOnlyDictionary<CanvasId, HashSet<CanvasId>> adjacency,
        HashSet<CanvasId> visiting)
    {
        if (!visiting.Add(current))
            return 0;

        if (!adjacency.TryGetValue(current, out var children) || children.Count == 0)
        {
            visiting.Remove(current);
            return 0;
        }

        var maxChildDepth = children.Max(child => MeasureMaxNestedDepthDfs(child, adjacency, visiting));
        visiting.Remove(current);
        return maxChildDepth + 1;
    }

    private static string FormatCycle(IReadOnlyList<CanvasId> cyclePath, IReadOnlyDictionary<CanvasId, MediaForgeCanvas> canvasById)
    {
        var labels = cyclePath.Select(id =>
            canvasById.TryGetValue(id, out var canvas)
                ? $"'{canvas.Name}' ({id})"
                : id.ToString());

        return string.Join(" -> ", labels);
    }
}
