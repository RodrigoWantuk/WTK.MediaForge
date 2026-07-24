using System.Collections.Immutable;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed record CanvasStateSnapshot
{
    public CanvasId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public FrameSize Size { get; init; }

    public ColorRgba BackgroundColor { get; init; } = ColorRgba.Black;

    /// <summary>
    /// Effects applied after this canvas has been fully composed.  Keeping the
    /// stack on the immutable state snapshot is important: a render frame must
    /// never observe a half-edited canvas effect collection.
    /// </summary>
    public ImmutableArray<EffectStateSnapshot> Effects { get; init; } = ImmutableArray<EffectStateSnapshot>.Empty;

    public ImmutableArray<DrawObjectStateSnapshot> Objects { get; init; } = ImmutableArray<DrawObjectStateSnapshot>.Empty;
}
