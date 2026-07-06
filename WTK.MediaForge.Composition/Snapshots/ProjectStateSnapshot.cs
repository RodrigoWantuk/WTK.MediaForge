using System.Collections.Immutable;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed record ProjectStateSnapshot
{
    public long Version { get; init; }

    public ImmutableArray<CanvasStateSnapshot> Canvases { get; init; } = ImmutableArray<CanvasStateSnapshot>.Empty;

    public ImmutableArray<RenderOutputStateSnapshot> Outputs { get; init; } = ImmutableArray<RenderOutputStateSnapshot>.Empty;

    public ImmutableArray<SourceDefinitionSnapshot> Sources { get; init; } = ImmutableArray<SourceDefinitionSnapshot>.Empty;
}
