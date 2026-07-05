using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public enum OutputRouteTransitionKind
{
    Cut,
    Fade
}

public sealed class OutputRouteTransition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public OutputRouteTransitionKind Kind { get; init; } = OutputRouteTransitionKind.Cut;

    public int DurationMs { get; init; }

    public static OutputRouteTransition Cut(string id, string displayName = "Cut") =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = OutputRouteTransitionKind.Cut,
            DurationMs = 0
        };

    public static OutputRouteTransition Fade(string id, int durationMs, string displayName = "Fade") =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = OutputRouteTransitionKind.Fade,
            DurationMs = durationMs
        };
}
