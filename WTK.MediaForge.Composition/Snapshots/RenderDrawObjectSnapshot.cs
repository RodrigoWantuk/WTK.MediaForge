using System.Collections.Immutable;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Composition.Scenes.Editing;

namespace WTK.MediaForge.Composition.Snapshots;

internal abstract class RenderDrawObjectSnapshot
{
    public DrawObjectId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public Transform2D Transform { get; init; }

    public NormalizedRect EffectiveCrop { get; init; } = NormalizedRect.Full;

    public float Opacity { get; init; } = 1f;

    public BlendMode BlendMode { get; init; } = BlendMode.Normal;

    public ImmutableArray<EffectStateSnapshot> Effects { get; init; } = [];
}

internal sealed class RenderSourceLayerDrawObjectSnapshot : RenderDrawObjectSnapshot
{
    public ImmutableArray<EffectStateSnapshot> SourceEffects { get; init; } = [];

    public SourceId SourceId { get; init; }

    public LayoutMode LayoutMode { get; init; } = LayoutMode.Fit;

    public ColorRgba LetterboxColor { get; init; } = ColorRgba.Transparent;

    public DisplayRotation? ContentRotationOverride { get; init; }

    public GpuFrameReference? BoundFrame { get; init; }
}

internal sealed class RenderTextDrawObjectSnapshot : RenderDrawObjectSnapshot
{
    public string Text { get; init; } = string.Empty;

    public string FontFamily { get; init; } = DrawObjects.TextDrawObject.DefaultFontFamily;

    public ColorRgba TextColor { get; init; } = ColorRgba.White;

    public float FontSize { get; init; } = 24f;
}

internal sealed class RenderSolidDrawObjectSnapshot : RenderDrawObjectSnapshot
{
    public ColorRgba FillColor { get; init; } = ColorRgba.Black;
}

internal sealed class RenderCanvasDrawObjectSnapshot : RenderDrawObjectSnapshot
{
    public CanvasId NestedCanvasId { get; init; }

    public SceneVersionBinding VersionBinding { get; init; } = SceneVersionBinding.Published;

    public SceneVersionId? NestedCanvasVersionId { get; init; }

    public ResolvedCanvasKey? NestedResolvedCanvasKey { get; init; }

    public RenderCanvasSnapshot? NestedCanvas { get; init; }
}
