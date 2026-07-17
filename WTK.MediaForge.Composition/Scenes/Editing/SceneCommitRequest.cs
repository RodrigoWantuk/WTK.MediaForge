namespace WTK.MediaForge.Composition.Scenes.Editing;

public sealed record SceneCommitRequest
{
    public bool AllowStaleBase { get; init; }

    public string? Reason { get; init; }

    public SceneApplyTransitionPolicy TransitionPolicy { get; init; } =
        SceneApplyTransitionPolicy.UseOutputRoutePolicy;
}
