namespace WTK.MediaForge.Composition.Scenes.Editing;

public enum SceneApplyTransitionKind
{
    UseOutputRoutePolicy = 0,
    Cut = 1,
    Fade = 2
}

public sealed record SceneApplyTransitionPolicy
{
    private SceneApplyTransitionPolicy(SceneApplyTransitionKind kind, TimeSpan duration)
    {
        Kind = kind;
        Duration = duration;
    }

    public static SceneApplyTransitionPolicy UseOutputRoutePolicy { get; } =
        new(SceneApplyTransitionKind.UseOutputRoutePolicy, TimeSpan.Zero);

    public SceneApplyTransitionKind Kind { get; }

    public TimeSpan Duration { get; }

    public static SceneApplyTransitionPolicy Cut() =>
        new(SceneApplyTransitionKind.Cut, TimeSpan.Zero);

    public static SceneApplyTransitionPolicy Fade(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Fade transition duration must be positive.");

        return new SceneApplyTransitionPolicy(SceneApplyTransitionKind.Fade, duration);
    }
}
