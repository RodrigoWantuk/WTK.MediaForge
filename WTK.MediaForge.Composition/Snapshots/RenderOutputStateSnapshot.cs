using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Snapshots;

public sealed class RenderOutputStateSnapshot
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
}
