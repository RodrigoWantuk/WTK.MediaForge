using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class ImageFileSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.ImageFile;

    public int SchemaVersion { get; init; } = 1;

    public string Path { get; init; } = string.Empty;
}
