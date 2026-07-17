using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed class SceneCommitPropagationPlanner(SceneDependencyGraph graph)
{
    public SceneCommitPropagationPlan Plan(
        CanvasId committedCanvasId,
        SceneVersionId oldVersionId,
        SceneVersionId newVersionId,
        SceneApplyTransitionPolicy transitionPolicy)
    {
        ArgumentNullException.ThrowIfNull(transitionPolicy);

        var direct = graph.GetDirectConsumers(committedCanvasId);
        var transitive = graph.GetTransitiveConsumers(committedCanvasId);
        var all = new HashSet<CanvasId> { committedCanvasId };
        foreach (var canvas in transitive)
            all.Add(canvas);

        return new SceneCommitPropagationPlan
        {
            CommittedCanvasId = committedCanvasId,
            OldVersionId = oldVersionId,
            NewVersionId = newVersionId,
            TransitionPolicy = transitionPolicy,
            AffectedCanvases = new AffectedCanvasSet
            {
                RootCanvasId = committedCanvasId,
                DirectConsumers = direct,
                TransitiveConsumers = transitive,
                AllAffected = all.ToArray()
            },
            AffectedOutputs = new AffectedOutputRouteSet
            {
                OutputRouteIds = graph.GetAffectedOutputs(committedCanvasId)
            }
        };
    }
}
