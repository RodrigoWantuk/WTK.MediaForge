using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Project;

public sealed class MediaForgeRenderOutput
{
    public RenderOutputId Id { get; set; } = RenderOutputId.New();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public RenderOutputTypeId TypeId { get; set; } = RenderOutputTypes.PreviewWindow;

    public int SchemaVersion { get; set; } = 1;

    public JsonObject Settings { get; set; } = new();

    public CanvasId CanvasId { get; set; }

    public FrameSize OutputSize { get; set; } = new(1920, 1080);

    public LayoutMode CanvasLayoutMode { get; set; } = LayoutMode.Fit;

    public ColorRgba LetterboxColor { get; set; } = ColorRgba.Black;

    public RenderColorSpace ColorSpace { get; set; } = RenderColorSpace.Srgb;

    public SceneVersionBinding SceneVersionBinding { get; set; } =
        SceneVersionBinding.Published;

    public OutputRouteTransition RouteTransition { get; set; } =
        OutputRouteTransition.Cut("cut");

    /// <summary>Optional portable audio routes associated with this video output.</summary>
    public List<AudioOutputRouteId> AudioOutputRouteIds { get; set; } = [];
}
