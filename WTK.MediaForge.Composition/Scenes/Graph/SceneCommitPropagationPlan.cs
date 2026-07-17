using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed record SceneCommitPropagationPlan
{
    public required CanvasId CommittedCanvasId { get; init; }

    public required SceneVersionId OldVersionId { get; init; }

    public required SceneVersionId NewVersionId { get; init; }

    public required AffectedCanvasSet AffectedCanvases { get; init; }

    public required AffectedOutputRouteSet AffectedOutputs { get; init; }

    public required SceneApplyTransitionPolicy TransitionPolicy { get; init; }

    public bool UsesTransition => TransitionPolicy.Kind != SceneApplyTransitionKind.Cut;
}
