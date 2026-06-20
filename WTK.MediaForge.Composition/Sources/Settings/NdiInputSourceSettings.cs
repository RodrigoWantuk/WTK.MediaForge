using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class NdiInputSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.NdiInput;

    public int SchemaVersion { get; init; } = 1;

    public string SourceName { get; init; } = string.Empty;
}
