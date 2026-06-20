using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class GeneratedSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.Generated;

    public int SchemaVersion { get; init; } = 1;

    public string GeneratorKind { get; init; } = string.Empty;
}
