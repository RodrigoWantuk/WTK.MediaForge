using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeCanvasPreset
{
    public int SchemaVersion { get; init; } = 1;

    public string Name { get; init; } = string.Empty;

    public CanvasId RootCanvasId { get; init; }

    public List<MediaForgeCanvas> Canvases { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];
}
