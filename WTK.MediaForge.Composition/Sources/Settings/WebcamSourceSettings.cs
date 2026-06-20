using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class WebcamSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.Webcam;

    public int SchemaVersion { get; init; } = 1;

    public string DeviceId { get; init; } = string.Empty;

    public int? PreferredWidth { get; init; }

    public int? PreferredHeight { get; init; }

    public double? PreferredFrameRate { get; init; }
}
