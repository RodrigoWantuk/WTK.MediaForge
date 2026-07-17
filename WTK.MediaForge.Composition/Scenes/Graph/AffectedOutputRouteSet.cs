using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Graph;

internal sealed record AffectedOutputRouteSet
{
    public IReadOnlyList<RenderOutputId> OutputRouteIds { get; init; } = Array.Empty<RenderOutputId>();
}
