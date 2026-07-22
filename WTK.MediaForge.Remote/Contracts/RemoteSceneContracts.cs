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

// Runtime-only credentials. This type is deliberately outside canonical project settings.
public sealed record RemoteSceneRuntimeCredentials(
    string? AccessToken = null,
    string? InvitationCode = null,
    string? TurnUsername = null,
    string? TurnCredential = null,
    string? SessionToken = null);

public sealed record RemoteSceneConnectionRequest(
    WebRtcConnectionOptions Connection,
    RemoteSceneRuntimeCredentials Credentials);
public sealed record RemoteScenePublishRequest(string StreamName, RenderOutputId OutputId, EncodedVideoProfile VideoProfile, bool IncludeAudio = false);
public sealed record RemoteSceneSubscribeRequest(string StreamName, bool IncludeAudio = false);

public sealed record RemoteSceneTelemetry(
    RemoteSceneConnectionState State,
    long BitrateBitsPerSecond,
    TimeSpan? RoundTripTime,
    double PacketLossPercent,
    long FramesDropped,
    bool RelayActive,
    string? FailureReason = null,
    TimeSpan? Jitter = null,
    string? SelectedCandidate = null,
    long FramesSent = 0,
    long FramesReceived = 0,
    long KeyFrames = 0,
    long ReconnectCount = 0);

public sealed class RemoteSceneFormatChangedEventArgs(
    int width,
    int height,
    EncodedVideoProfile profile,
    long generation) : EventArgs
{
    public int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    public EncodedVideoProfile Profile { get; } = profile ?? throw new ArgumentNullException(nameof(profile));
    public long Generation { get; } = generation > 0 ? generation : throw new ArgumentOutOfRangeException(nameof(generation));
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
    /// <summary>
    /// Transfers one ownership reference to the publisher. The publisher must dispose the
    /// lease after native send completion or rejection. Cancellation does not return ownership.
    /// A bounded queue applies <see cref="QueuePolicy"/> and the call must complete within its timeout.
    /// </summary>
    ValueTask SendVideoPacketAsync(EncodedVideoPacketLease packet, CancellationToken cancellationToken);
    RemoteScenePacketQueuePolicy QueuePolicy { get; }
    event EventHandler? KeyFrameRequested;
}

public interface IRemoteSceneSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Yields one owned lease at a time. The consumer must dispose every lease; undisposed
    /// leases occupy bounded queue capacity and eventually invoke the slow-consumer policy.
    /// </summary>
    IAsyncEnumerable<EncodedVideoPacketLease> VideoPackets { get; }
    RemoteScenePacketQueuePolicy QueuePolicy { get; }
    RemoteSceneFormatChangedEventArgs? CurrentFormat { get; }
    event EventHandler<RemoteSceneFormatChangedEventArgs>? FormatChanged;
    ValueTask RequestKeyFrameAsync(CancellationToken cancellationToken);
}

public enum RemoteSceneSlowConsumerPolicy
{
    DropDeltaFramesUntilKeyFrame,
    FailSession
}

public sealed record RemoteScenePacketQueuePolicy
{
    public int Capacity { get; init; } = 8;
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public RemoteSceneSlowConsumerPolicy SlowConsumerPolicy { get; init; } =
        RemoteSceneSlowConsumerPolicy.DropDeltaFramesUntilKeyFrame;

    public void Validate()
    {
        if (Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(Capacity));
        if (OperationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(OperationTimeout));
    }
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
