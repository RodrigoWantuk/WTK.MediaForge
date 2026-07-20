using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Remote;

public enum RemoteSceneConnectionState
{
    Stopped,
    Connecting,
    ConnectedDirect,
    ConnectedRelay,
    Reconnecting,
    Failed
}

public sealed record WebRtcIceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null)
{
    public void Validate()
    {
        if (Urls is null || Urls.Count == 0 || Urls.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A WebRTC ICE server requires at least one URL.", nameof(Urls));
    }
}

public sealed record WebRtcConnectionOptions
{
    public required Uri SignalingServer { get; init; }
    public IReadOnlyList<WebRtcIceServer> IceServers { get; init; } = Array.Empty<WebRtcIceServer>();

    public void Validate()
    {
        if (!SignalingServer.IsAbsoluteUri || SignalingServer.Scheme is not ("https" or "wss"))
            throw new ArgumentException("Remote Scene signaling requires an absolute HTTPS or WSS URI.", nameof(SignalingServer));
        foreach (var server in IceServers)
            server.Validate();
    }
}

// Persist only a profile reference; signaling/TURN credentials are injected at connection time.
public sealed record RemoteSceneSinkSettings
{
    public required string StreamName { get; init; }
    public required RenderOutputId OutputId { get; init; }
    public required string ConnectionProfileId { get; init; }
    public required EncodedVideoProfile VideoProfile { get; init; }
    public bool IncludeAudio { get; init; }
}

public sealed record RemoteSceneSourceSettings
{
    public required string StreamName { get; init; }
    public required string ConnectionProfileId { get; init; }
    public bool IncludeAudio { get; init; }
}

public sealed record RemoteSceneConnectionRequest(WebRtcConnectionOptions Connection, string InvitationCode);
public sealed record RemoteScenePublishRequest(string StreamName, RenderOutputId OutputId, EncodedVideoProfile VideoProfile, bool IncludeAudio = false);
public sealed record RemoteSceneSubscribeRequest(string StreamName, bool IncludeAudio = false);

public sealed record RemoteSceneTelemetry(
    RemoteSceneConnectionState State,
    long BitrateBitsPerSecond,
    TimeSpan? RoundTripTime,
    double PacketLossPercent,
    long FramesDropped,
    bool RelayActive,
    string? FailureReason = null);

public sealed class EncodedVideoPacketReceivedEventArgs(EncodedVideoPacket packet) : EventArgs
{
    public EncodedVideoPacket Packet { get; } = packet ?? throw new ArgumentNullException(nameof(packet));
}

public sealed class RemoteSceneFormatChangedEventArgs(int width, int height, EncodedVideoProfile profile) : EventArgs
{
    public int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    public EncodedVideoProfile Profile { get; } = profile ?? throw new ArgumentNullException(nameof(profile));
}

public interface IRemoteSceneTransport : IAsyncDisposable
{
    Task<IRemoteSceneSession> ConnectAsync(RemoteSceneConnectionRequest request, CancellationToken cancellationToken);
}

public interface IRemoteSceneSession : IAsyncDisposable
{
    RemoteSceneConnectionState State { get; }
    RemoteSceneTelemetry Telemetry { get; }
    Task<IRemoteScenePublisher> PublishAsync(RemoteScenePublishRequest request, CancellationToken cancellationToken);
    Task<IRemoteSceneSubscriber> SubscribeAsync(RemoteSceneSubscribeRequest request, CancellationToken cancellationToken);
}

public interface IRemoteScenePublisher : IAsyncDisposable
{
    ValueTask SendVideoPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken);
    ValueTask RequestKeyFrameAsync(CancellationToken cancellationToken);
}

public interface IRemoteSceneSubscriber : IAsyncDisposable
{
    event EventHandler<EncodedVideoPacketReceivedEventArgs>? VideoPacketReceived;
    event EventHandler<RemoteSceneFormatChangedEventArgs>? FormatChanged;
    ValueTask RequestKeyFrameAsync(CancellationToken cancellationToken);
}

public static class RemoteSceneRequestValidator
{
    public static void Validate(RemoteScenePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IncludeAudio)
            throw new NotSupportedException("Remote Scene audio is not implemented. V1 is hardware H.264 video only.");
        ValidateVideoProfile(request.VideoProfile);
    }

    public static void Validate(RemoteSceneSubscribeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IncludeAudio)
            throw new NotSupportedException("Remote Scene audio is not implemented. V1 is hardware H.264 video only.");
    }

    private static void ValidateVideoProfile(EncodedVideoProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException("Remote Scene V1 requires H.264 packets from the hardware encoder.");
    }
}
