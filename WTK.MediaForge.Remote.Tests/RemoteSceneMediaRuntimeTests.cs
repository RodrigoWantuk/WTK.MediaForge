using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Remote;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class RemoteSceneMediaRuntimeTests
{
    [Fact]
    public void Jitter_buffer_reorders_packets_and_disposes_every_owned_lease()
    {
        var releases = 0;
        using var jitter = new RemoteSceneJitterBuffer(capacity: 4, targetDepth: 1);
        jitter.Enqueue(Lease(3, false, () => releases++));
        jitter.Enqueue(Lease(1, true, () => releases++));
        jitter.Enqueue(Lease(2, false, () => releases++));

        var times = new List<long>();
        while (jitter.TryDequeue(draining: true, out var lease))
        {
            using (lease)
                times.Add(lease!.Packet.PresentationTime.Ticks);
        }

        Assert.Equal([1, 2, 3], times);
        Assert.Equal(3, releases);
    }

    [Fact]
    public void Full_jitter_buffer_drops_delta_packet_but_retains_incoming_keyframe()
    {
        var releases = 0;
        using var jitter = new RemoteSceneJitterBuffer(capacity: 2, targetDepth: 1);
        jitter.Enqueue(Lease(1, true, () => releases++));
        jitter.Enqueue(Lease(2, false, () => releases++));
        jitter.Enqueue(Lease(3, false, () => releases++));
        jitter.Enqueue(Lease(4, true, () => releases++));

        var keyframes = new List<bool>();
        while (jitter.TryDequeue(draining: true, out var lease))
        {
            using (lease)
                keyframes.Add(lease!.Packet.IsKeyFrame);
        }

        Assert.Equal(2, jitter.DroppedPackets);
        Assert.Equal([true, true], keyframes);
        Assert.Equal(4, releases);
    }

    [Fact]
    public void Clearing_jitter_for_format_change_disposes_all_old_generation_packets()
    {
        var releases = 0;
        using var jitter = new RemoteSceneJitterBuffer(capacity: 4, targetDepth: 2);
        jitter.Enqueue(Lease(1, true, () => releases++));
        jitter.Enqueue(Lease(2, false, () => releases++));

        Assert.Equal(2, jitter.Clear());
        Assert.Equal(2, releases);
        Assert.False(jitter.TryDequeue(draining: true, out _));
    }

    [Fact]
    public async Task Packet_sink_rejects_unproved_packets_and_forwards_keyframe_feedback()
    {
        var publisher = new RecordingPublisher();
        var sink = new RemoteScenePacketSink(
            new RemoteSceneOutputSettings
            {
                SignalingEndpoint = "wss://signal.example.test",
                StreamName = "program"
            },
            new RecordingPublisherFactory(publisher));
        var requests = 0;
        sink.KeyFrameRequested += (_, _) => requests++;
        await sink.StartAsync(new EncodedPacketSinkContext
        {
            Codec = EncodedVideoCodec.H264,
            Size = new FrameSize(1920, 1080),
            FramesPerSecond = 60
        }, CancellationToken.None);

        publisher.RaiseKeyFrameRequested();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.WritePacketAsync(new EncodedVideoPacket
            {
                Data = new byte[] { 0, 0, 0, 1, 0x65 },
                Codec = EncodedVideoCodec.H264,
                IsKeyFrame = true
            }, CancellationToken.None).AsTask());
        await sink.StopAsync(CancellationToken.None);

        Assert.Contains("BackendOutputValidated", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, requests);
        Assert.True(publisher.Disposed);
    }

    [Fact]
    public async Task Packet_sink_start_is_idempotent_and_stop_resets_state_after_dispose_failure()
    {
        var failing = new RecordingPublisher { FailDispose = true };
        var factory = new RecordingPublisherFactory(failing);
        var sink = new RemoteScenePacketSink(
            new RemoteSceneOutputSettings
            {
                SignalingEndpoint = "wss://signal.example.test",
                StreamName = "program"
            },
            factory);
        var context = new EncodedPacketSinkContext
        {
            Codec = EncodedVideoCodec.H264,
            Size = new FrameSize(1920, 1080),
            FramesPerSecond = 60
        };

        await sink.StartAsync(context, CancellationToken.None);
        await sink.StartAsync(context, CancellationToken.None);
        Assert.Equal(1, factory.CreateCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.StopAsync(CancellationToken.None).AsTask());

        failing.FailDispose = false;
        await sink.StartAsync(context, CancellationToken.None);
        Assert.Equal(2, factory.CreateCount);
        await sink.StopAsync(CancellationToken.None);
    }

    private static EncodedVideoPacketLease Lease(long ticks, bool keyframe, Action release) =>
        EncodedVideoPacketLease.Create(new EncodedVideoPacket
        {
            Data = new byte[] { 1 },
            Codec = EncodedVideoCodec.H264,
            PresentationTime = TimeSpan.FromTicks(ticks),
            IsKeyFrame = keyframe
        }, release);

    private sealed class RecordingPublisherFactory(IRemoteScenePublisher publisher) : IRemoteScenePublisherFactory
    {
        public int CreateCount { get; private set; }

        public ValueTask<IRemoteScenePublisher> CreateAsync(
            RemoteSceneOutputSettings settings,
            EncodedPacketSinkContext context,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            return ValueTask.FromResult(publisher);
        }
    }

    private sealed class RecordingPublisher : IRemoteScenePublisher
    {
        public RemoteScenePacketQueuePolicy QueuePolicy { get; } = new();
        public event EventHandler? KeyFrameRequested;
        public bool Disposed { get; private set; }
        public bool FailDispose { get; set; }

        public ValueTask SendVideoPacketAsync(EncodedVideoPacketLease packet, CancellationToken cancellationToken)
        {
            packet.Dispose();
            return ValueTask.CompletedTask;
        }

        public void RaiseKeyFrameRequested() => KeyFrameRequested?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return FailDispose
                ? ValueTask.FromException(new InvalidOperationException("publisher dispose"))
                : ValueTask.CompletedTask;
        }
    }
}
