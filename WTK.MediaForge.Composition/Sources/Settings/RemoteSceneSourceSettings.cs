using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class RemoteSceneSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.RemoteScene;
    public int SchemaVersion { get; init; } = 1;
    public string Provider { get; init; } = "webrtc";
    public string SignalingEndpoint { get; init; } = string.Empty;
    public string StreamName { get; init; } = string.Empty;
    public string SessionPolicy { get; init; } = "invitation";
    public IReadOnlyList<string> CodecPreferences { get; init; } = ["h264"];
    public int PreferredWidth { get; init; } = 1920;
    public int PreferredHeight { get; init; } = 1080;
    public int ReconnectAttempts { get; init; } = 5;
    public int ReconnectDelayMs { get; init; } = 1000;
}
