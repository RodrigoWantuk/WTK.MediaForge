using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class IpCameraSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.IpCamera;

    public int SchemaVersion { get; init; } = 1;

    public string Url { get; init; } = string.Empty;

    public RtspTransportMode Transport { get; init; } = RtspTransportMode.Tcp;
}
