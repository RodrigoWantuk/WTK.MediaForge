using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed class RenderOutputStateSnapshot
{
    public RenderOutputId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public RenderOutputTypeId TypeId { get; init; } = RenderOutputTypes.PreviewWindow;

    public int SchemaVersion { get; init; } = 1;

    public JsonObject Settings { get; init; } = new();

    public CanvasId CanvasId { get; init; }

    public FrameSize OutputSize { get; init; }

    public LayoutMode CanvasLayoutMode { get; init; } = LayoutMode.Fit;

    public ColorRgba LetterboxColor { get; init; } = ColorRgba.Black;

    public RenderColorSpace ColorSpace { get; init; } = RenderColorSpace.Srgb;

    public SceneVersionBinding SceneVersionBinding { get; init; } =
        SceneVersionBinding.Published;

    public OutputRouteTransitionKind RouteTransitionKind { get; init; } =
        OutputRouteTransitionKind.Cut;

    public CanvasId? PreviousCanvasId { get; init; }

    public float RouteTransitionProgress { get; init; } = 1f;
}
