namespace WTK.MediaForge.Composition.Scenes.Editing;

public enum SceneApplyTransitionKind
{
    UseOutputRoutePolicy = 0,
    Cut = 1,
    Fade = 2
}

public sealed record SceneApplyTransitionPolicy
{
    public static SceneApplyTransitionPolicy UseOutputRoutePolicy { get; } = new();

    public SceneApplyTransitionKind Kind { get; init; } = SceneApplyTransitionKind.UseOutputRoutePolicy;

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(250);
}
