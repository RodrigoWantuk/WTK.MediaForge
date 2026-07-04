namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeSourcePreset
{
    public int SchemaVersion { get; init; } = 1;

    public string Name { get; init; } = string.Empty;

    public MediaForgeSourceDefinition Source { get; init; } = new();

    public Dictionary<string, string> Metadata { get; init; } = [];
}
