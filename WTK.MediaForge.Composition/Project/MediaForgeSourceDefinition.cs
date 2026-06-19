using System.Text.Json.Nodes;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Project;

public sealed class MediaForgeSourceDefinition
{
    public SourceId Id { get; set; } = SourceId.New();

    public string Name { get; set; } = string.Empty;

    public MediaSourceTypeId TypeId { get; set; } = MediaSourceTypeId.DesktopCapture;

    public int SchemaVersion { get; set; } = 1;

    public JsonObject Settings { get; set; } = new();
}
