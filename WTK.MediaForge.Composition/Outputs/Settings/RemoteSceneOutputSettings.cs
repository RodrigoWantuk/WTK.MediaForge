using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class RemoteSceneOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.RemoteScene;
    public int SchemaVersion { get; init; } = 1;
    public string Provider { get; init; } = "webrtc";
    public string SignalingEndpoint { get; init; } = string.Empty;
    public string StreamName { get; init; } = string.Empty;
    public string SessionPolicy { get; init; } = "invitation";
    public IReadOnlyList<string> CodecPreferences { get; init; } = ["h264"];
    public EncodedVideoProfile Video { get; init; } = EncodedVideoProfile.DefaultH264;
    public int ReconnectAttempts { get; init; } = 5;
    public int ReconnectDelayMs { get; init; } = 1000;
}
