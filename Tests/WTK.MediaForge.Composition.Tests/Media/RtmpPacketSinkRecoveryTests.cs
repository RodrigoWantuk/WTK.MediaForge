using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Media;

public sealed class RtmpPacketSinkRecoveryTests
{
    [Fact]
    public async Task Keyframe_is_retried_after_transport_reconnect()
    {
        var initial = new TestRtmpTransport(failFirstSend: true);
        var recovered = new TestRtmpTransport();
        var transports = new Queue<IRtmpTransport>([initial, recovered]);
        await using var sink = new RtmpPacketSink(
            "rtmp://localhost/live/test",
            () => transports.Dequeue());
        var statuses = new List<RtmpOutputRuntimeStatus>();
        sink.StatusChanged += (_, args) => statuses.Add(args.Status);

        await sink.StartAsync(CreateContext(), CancellationToken.None);
        await sink.WritePacketAsync(CreateKeyFrame(), CancellationToken.None);

        Assert.Equal(RtmpOutputRuntimeStatus.Live, sink.Status);
        Assert.Contains(RtmpOutputRuntimeStatus.Recovering, statuses);
        Assert.Equal(2, recovered.SentPackets.Count);
        Assert.Equal(0, sink.DroppedPacketsDuringRecovery);
    }

    [Fact]
    public async Task Non_keyframe_is_dropped_honestly_after_reconnect()
    {
        var initial = new TestRtmpTransport(failFirstSend: true);
        var recovered = new TestRtmpTransport();
        var transports = new Queue<IRtmpTransport>([initial, recovered]);
        await using var sink = new RtmpPacketSink(
            "rtmp://localhost/live/test",
            () => transports.Dequeue());

        await sink.StartAsync(CreateContext(), CancellationToken.None);
        await sink.WritePacketAsync(CreateKeyFrame().WithKeyFrame(false), CancellationToken.None);

        Assert.Equal(RtmpOutputRuntimeStatus.Live, sink.Status);
        Assert.Equal(1, sink.DroppedPacketsDuringRecovery);
        Assert.Empty(recovered.SentPackets);
    }

    private static EncodedPacketSinkContext CreateContext() => new()
    {
        Codec = EncodedVideoCodec.H264,
        Size = new FrameSize(320, 180),
        FramesPerSecond = 30
    };

    private static EncodedVideoPacket CreateKeyFrame() => new()
    {
        Data = new byte[]
        {
            0, 0, 0, 1, 0x67, 0x64, 0, 0x1F,
            0, 0, 0, 1, 0x68, 0xEE, 0x3C, 0x80,
            0, 0, 0, 1, 0x65, 0x88, 0x84
        },
        Codec = EncodedVideoCodec.H264,
        BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
        IsKeyFrame = true,
        Duration = TimeSpan.FromMilliseconds(33),
        Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
            nameof(RtmpPacketSinkRecoveryTests),
            "TestBackend",
            MediaForgeCapabilityCatalog.RtmpNetworkOutputProof)
    };

    private sealed class TestRtmpTransport(bool failFirstSend = false) : IRtmpTransport
    {
        private bool _failFirstSend = failFirstSend;

        public string Url => "rtmp://localhost/live/test";

        public bool IsConnected { get; private set; }

        public List<FlvPacket> SentPackets { get; } = [];

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(FlvPacket packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failFirstSend)
            {
                _failFirstSend = false;
                throw new IOException("Synthetic disconnect.");
            }

            SentPackets.Add(packet);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => IsConnected = false;
    }
}

file static class EncodedVideoPacketTestExtensions
{
    public static EncodedVideoPacket WithKeyFrame(
        this EncodedVideoPacket packet,
        bool isKeyFrame) => new()
    {
        Data = packet.Data,
        Codec = packet.Codec,
        BitstreamFormat = packet.BitstreamFormat,
        PresentationTime = packet.PresentationTime,
        Duration = packet.Duration,
        IsKeyFrame = isKeyFrame,
        Evidence = packet.Evidence,
        CodecConfiguration = packet.CodecConfiguration
    };
}
