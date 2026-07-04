using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeScenePackage
{
    public int SchemaVersion { get; init; } = 1;

    public string ExportedWithVersion { get; init; } = "1.0.0";

    public string Name { get; init; } = string.Empty;

    public CanvasId RootCanvasId { get; init; }

    public List<MediaForgeSourceDefinition> SourceDefinitions { get; init; } = [];

    public List<MediaForgeCanvas> Canvases { get; init; } = [];

    public List<MediaForgeRenderOutput> Outputs { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];
}
