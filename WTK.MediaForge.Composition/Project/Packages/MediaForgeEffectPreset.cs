using WTK.MediaForge.Composition.Effects;

namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeEffectPreset
{
    public int SchemaVersion { get; init; } = 1;

    public string Name { get; init; } = string.Empty;

    public List<MediaForgeEffect> Effects { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];
}
