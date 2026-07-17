using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class MediaForgeRenderGraphNode
{
    public required MediaForgeRenderGraphNodeKind Kind { get; init; }

    public required string Key { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public RenderOutputId? OutputId { get; init; }

    public CanvasId? CanvasId { get; init; }

    public CanvasId? PreviousCanvasId { get; init; }
}
