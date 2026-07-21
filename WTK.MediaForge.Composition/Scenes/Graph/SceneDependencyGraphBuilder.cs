using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal static class SceneDependencyGraphBuilder
{
    public static SceneDependencyGraph Build(MediaForgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var nodes = project.Canvases.Select(static canvas => new SceneDependencyNode
        {
            CanvasId = canvas.Id,
            Name = canvas.Name
        });

        var edges = project.Canvases.SelectMany(static canvas =>
            canvas.Objects
                .OfType<CanvasDrawObject>()
                .Select(layer => new SceneDependencyEdge
                {
                    ConsumerCanvasId = canvas.Id,
                    NestedCanvasId = layer.NestedCanvasId,
                    LayerId = layer.Id
                }));

        var outputs = project.Outputs
            .Where(static output => output.Enabled)
            .GroupBy(static output => output.CanvasId)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<RenderOutputId>)group.Select(static output => output.Id).ToArray());

        return new SceneDependencyGraph(nodes, edges, outputs);
    }

    public static SceneDependencyGraph Build(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        var nodes = projectState.Canvases.Select(static canvas => new SceneDependencyNode
        {
            CanvasId = canvas.Id,
            Name = canvas.Name
        });

        var edges = projectState.Canvases.SelectMany(static canvas =>
            canvas.Objects
                .OfType<CanvasDrawObjectSnapshot>()
                .Select(layer => new SceneDependencyEdge
                {
                    ConsumerCanvasId = canvas.Id,
                    NestedCanvasId = layer.NestedCanvasId,
                    LayerId = layer.Id
                }));

        var outputs = projectState.Outputs
            .GroupBy(static output => output.CanvasId)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<RenderOutputId>)group.Select(static output => output.Id).ToArray());

        return new SceneDependencyGraph(nodes, edges, outputs);
    }
}
