using System.Text.Json.Nodes;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

public sealed class SourceDefinitionSnapshot
{
    public SourceId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public MediaSourceTypeId TypeId { get; init; } = MediaSourceTypeId.DesktopCapture;

    public int SchemaVersion { get; init; } = 1;

    public JsonObject Settings { get; init; } = new();
}
