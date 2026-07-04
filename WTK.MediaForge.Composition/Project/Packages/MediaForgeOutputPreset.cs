namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeOutputPreset
{
    public int SchemaVersion { get; init; } = 1;

    public string Name { get; init; } = string.Empty;

    public MediaForgeRenderOutput Output { get; init; } = new();

    public Dictionary<string, string> Metadata { get; init; } = [];
}
