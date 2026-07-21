using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal static class SceneVersionDependencyResolver
{
    public static SceneVersionDependencyResolution Resolve(
        IEnumerable<SceneVersionId> directVersions,
        IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(directVersions);
        ArgumentNullException.ThrowIfNull(snapshots);

        var roots = directVersions.Distinct().ToHashSet();
        var visited = new HashSet<SceneVersionId>();
        var dependencies = new HashSet<SceneVersionId>();
        var failures = new HashSet<SceneVersionDependencyFailure>();

        foreach (var root in roots)
            Visit(root, root, snapshots, visited, dependencies, failures);

        dependencies.ExceptWith(roots);
        return new SceneVersionDependencyResolution(dependencies, failures);
    }

    private static void Visit(
        SceneVersionId root,
        SceneVersionId current,
        IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> snapshots,
        ISet<SceneVersionId> visited,
        ISet<SceneVersionId> dependencies,
        ISet<SceneVersionDependencyFailure> failures)
    {
        if (!visited.Add(current))
            return;

        if (!snapshots.TryGetValue(current, out var snapshot))
        {
            failures.Add(new SceneVersionDependencyFailure(root, current, null));
            return;
        }

        foreach (var nested in snapshot.Objects.OfType<CanvasDrawObjectSnapshot>())
        {
            nested.VersionBinding.Validate();
            if (nested.VersionBinding.Kind != SceneVersionBindingKind.ExplicitVersion ||
                nested.VersionBinding.ExplicitVersionId is not { } dependency)
            {
                continue;
            }

            if (!snapshots.TryGetValue(dependency, out var dependencySnapshot) ||
                dependencySnapshot.Id != nested.NestedCanvasId)
            {
                failures.Add(new SceneVersionDependencyFailure(root, dependency, nested.NestedCanvasId));
                continue;
            }

            dependencies.Add(dependency);
            Visit(root, dependency, snapshots, visited, dependencies, failures);
        }
    }
}

internal sealed record SceneVersionDependencyResolution(
    IReadOnlySet<SceneVersionId> Dependencies,
    IReadOnlySet<SceneVersionDependencyFailure> Failures);

internal readonly record struct SceneVersionDependencyFailure(
    SceneVersionId RootVersionId,
    SceneVersionId MissingVersionId,
    Core.Identifiers.CanvasId? ExpectedCanvasId);
