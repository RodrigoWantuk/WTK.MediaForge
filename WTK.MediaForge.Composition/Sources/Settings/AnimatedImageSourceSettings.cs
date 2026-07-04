using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class AnimatedImageSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.AnimatedImage;

    public int SchemaVersion { get; init; } = 1;

    public string Path { get; init; } = string.Empty;

    public bool Loop { get; init; } = true;

    public double? PreferredFrameRate { get; init; }
}
