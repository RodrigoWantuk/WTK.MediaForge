using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed record AffectedCanvasSet
{
    public required CanvasId RootCanvasId { get; init; }

    public IReadOnlyList<CanvasId> DirectConsumers { get; init; } = Array.Empty<CanvasId>();

    public IReadOnlyList<CanvasId> TransitiveConsumers { get; init; } = Array.Empty<CanvasId>();

    public IReadOnlyList<CanvasId> AllAffected { get; init; } = Array.Empty<CanvasId>();
}
