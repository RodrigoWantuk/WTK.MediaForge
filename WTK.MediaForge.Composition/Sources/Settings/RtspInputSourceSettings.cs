using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public enum RtspTransportMode
{
    Tcp = 0,
    Udp = 1
}

public sealed class RtspInputSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.RtspInput;

    public int SchemaVersion { get; init; } = 1;

    public string Url { get; init; } = string.Empty;

    public RtspTransportMode Transport { get; init; } = RtspTransportMode.Tcp;
}
