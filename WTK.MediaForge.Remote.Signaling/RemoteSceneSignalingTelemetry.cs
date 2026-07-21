using System.Diagnostics.Metrics;

namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingTelemetry : IDisposable
{
    private readonly Meter _meter = new("WTK.MediaForge.Remote.Signaling");
    private readonly Counter<long> _messages;
    private readonly Counter<long> _rejections;
    private readonly UpDownCounter<long> _connections;

    public RemoteSceneSignalingTelemetry()
    {
        _messages = _meter.CreateCounter<long>("remote_scene.signaling.messages");
        _rejections = _meter.CreateCounter<long>("remote_scene.signaling.rejections");
        _connections = _meter.CreateUpDownCounter<long>("remote_scene.signaling.connections");
    }

    public void ConnectionOpened(RemoteScenePeerRole role) => _connections.Add(1, new KeyValuePair<string, object?>("role", role.ToString()));
    public void ConnectionClosed(RemoteScenePeerRole role) => _connections.Add(-1, new KeyValuePair<string, object?>("role", role.ToString()));
    public void MessageAccepted(RemoteScenePeerRole role, RemoteSceneSignalingMessageKind kind) =>
        _messages.Add(1, new("role", role.ToString()), new("kind", kind.ToString()));
    public void MessageRejected(string reason) => _rejections.Add(1, new KeyValuePair<string, object?>("reason", reason));
    public void Dispose() => _meter.Dispose();
}
