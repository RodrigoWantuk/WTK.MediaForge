using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using Xunit;
using System.Buffers.Binary;

namespace WTK.MediaForge.Composition.Tests.Media;

public sealed class EncodedOutputPipelineTests
{
    [Fact]
    public async Task Recording_mp4_public_sink_rejects_packets_without_backend_validation()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_blocked_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4Sink(outputPath);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await sink.WritePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None));

            Assert.Contains("BackendOutputValidated", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Rtmp_public_sink_is_not_enabled_without_prototype_opt_in()
    {
        var sink = new RtmpSink("rtmp://127.0.0.1/live/blocked");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None));
    }

    [Fact]
    public void Encoded_packet_sinks_do_not_implement_render_output_sink()
    {
        Assert.False(typeof(IRenderOutputSink).IsAssignableFrom(typeof(RecordingMp4PacketSink)));
        Assert.False(typeof(IRenderOutputSink).IsAssignableFrom(typeof(RecordingMp4Sink)));
        Assert.False(typeof(IRenderOutputSink).IsAssignableFrom(typeof(RtmpPacketSink)));
        Assert.False(typeof(IRenderOutputSink).IsAssignableFrom(typeof(RtmpSink)));
    }

    [Fact]
    public async Task Recording_mp4_prototype_muxer_writes_experimental_file_structure()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_{Guid.NewGuid():N}.mp4");
        try
        {
            var audit = new CollectingMediaTransportAuditSink();
            await using var sink = new RecordingMp4Sink(outputPath, audit, allowPrototypeMuxer: true);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var packets = CreateSyntheticH264Packets(frameCount: 60);
            foreach (var packet in packets)
                await sink.WritePacketAsync(packet, CancellationToken.None);

            await sink.StopAsync(CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(IsoBmffMp4Writer.HasExperimentalBoxStructure(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 256);
            Assert.All(
                audit.Events.Where(static e => e.Kind == MediaTransportAuditEventKind.EncodedPacketProduced),
                static e => Assert.Equal(MediaTransportAuditEvidenceKind.Prototype, e.EvidenceKind));
            Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Rtmp_sink_receives_flv_tags_from_shared_encoder()
    {
        var router = new EncodedOutputRouter(new TestHardwareVideoEncoder());
        var rtmpSink = new RtmpPacketSink("rtmp://127.0.0.1/live/test", allowPrototypeTransport: true);
        router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

        await rtmpSink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

        var packet = CreateSyntheticH264Packets(1).Single();
        await router.RoutePacketAsync(packet, CancellationToken.None);
        await router.FlushAsync(CancellationToken.None);

        Assert.NotEmpty(rtmpSink.SentPacketsForTests);
        Assert.Equal(packet.Data, rtmpSink.SentPacketsForTests[0].Data);
        Assert.Equal(packet.BitstreamFormat, rtmpSink.SentPacketsForTests[0].BitstreamFormat);

        await rtmpSink.DisposeAsync();
        await router.DisposeAsync();
    }

    [Fact]
    public async Task Recording_mp4_product_muxer_writes_trusted_backend_validated_packets_without_prototype_audit()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_product_{Guid.NewGuid():N}.mp4");
        try
        {
            var audit = new CollectingMediaTransportAuditSink();
            await using var sink = new RecordingMp4Sink(outputPath, audit);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var packets = CreateSyntheticH264Packets(
                frameCount: 60,
                evidence: EncodedVideoPacketEvidence.CreateBackendOutputValidated(
                    nameof(EncodedOutputPipelineTests),
                    "TestBackend",
                    MediaForgeCapabilityCatalog.Mp4RecordingProof));
            foreach (var packet in packets)
                await sink.WritePacketAsync(packet, CancellationToken.None);

            await sink.StopAsync(CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(IsoBmffMp4Writer.HasValidH264BoxStructure(
                outputPath,
                new IsoBmffMp4Writer.TrackMetadata(640, 360),
                minimumSampleCount: 60));
            Assert.True(new FileInfo(outputPath).Length > 256);
            Assert.DoesNotContain(
                audit.Events,
                static e => e.EvidenceKind == MediaTransportAuditEvidenceKind.Prototype);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Recording_mp4_rejects_h264_packet_without_explicit_bitstream_format()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_unknown_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4PacketSink(outputPath, null, allowPrototypeMuxer: true);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var packet = new EncodedVideoPacket
            {
                Codec = EncodedVideoCodec.H264,
                PresentationTime = TimeSpan.Zero,
                Data = CreateKeyFrameAnnexB(),
                IsKeyFrame = true
            };

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await sink.WritePacketAsync(packet, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Recording_mp4_sink_rejects_restart_after_stop_and_start_after_dispose()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_lifecycle_{Guid.NewGuid():N}.mp4");
        try
        {
            var sink = new RecordingMp4PacketSink(outputPath, null, allowPrototypeMuxer: true);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);
            await sink.WritePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
            await sink.StopAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None));

            await sink.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Recording_mp4_rejects_annex_b_without_sps_pps_or_codec_configuration()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_no_config_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4PacketSink(outputPath, null, allowPrototypeMuxer: true);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            await sink.WritePacketAsync(
                new EncodedVideoPacket
                {
                    Codec = EncodedVideoCodec.H264,
                    BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
                    PresentationTime = TimeSpan.Zero,
                    Duration = TimeSpan.FromMilliseconds(33),
                    Data = CreatePFrameAnnexB(),
                    IsKeyFrame = true
                },
                CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sink.StopAsync(CancellationToken.None));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Recording_mp4_product_muxer_does_not_leave_final_file_when_finalize_fails()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_atomic_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4PacketSink(outputPath);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            await sink.WritePacketAsync(
                new EncodedVideoPacket
                {
                    Codec = EncodedVideoCodec.H264,
                    BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
                    PresentationTime = TimeSpan.Zero,
                    Duration = TimeSpan.FromMilliseconds(33),
                    Data = CreatePFrameAnnexB(),
                    IsKeyFrame = false,
                    Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
                        nameof(EncodedOutputPipelineTests),
                        "TestBackend",
                        MediaForgeCapabilityCatalog.Mp4RecordingProof)
                },
                CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sink.StopAsync(CancellationToken.None));

            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }


    [Fact]
    public void Recording_mp4_writer_rejects_null_packet_with_diagnostic()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_null_{Guid.NewGuid():N}.mp4");
        try
        {
            var packets = new List<EncodedVideoPacket> { null! };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                IsoBmffMp4Writer.WriteMp4(outputPath, packets));

            Assert.Contains("null encoded packet", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void Recording_mp4_writer_records_real_mdat_offset_and_minf_video_header()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_tables_{Guid.NewGuid():N}.mp4");
        try
        {
            IsoBmffMp4Writer.WriteMp4(outputPath, CreateSyntheticH264Packets(3));

            var bytes = File.ReadAllBytes(outputPath);
            var mdatTypeOffset = FindBoxTypeOffset(bytes, "mdat");
            var expectedMdatPayloadOffset = checked((uint)(mdatTypeOffset + 4));

            Assert.Equal(expectedMdatPayloadOffset, ReadFirstChunkOffset(bytes));
            Assert.True(IsBoxNestedInside(bytes, childType: "vmhd", parentType: "minf"));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Rtmp_rejects_h264_packet_without_explicit_bitstream_format()
    {
        var sink = new RtmpPacketSink("rtmp://127.0.0.1/live/unknown", allowPrototypeTransport: true);
        try
        {
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var packet = new EncodedVideoPacket
            {
                Codec = EncodedVideoCodec.H264,
                PresentationTime = TimeSpan.Zero,
                Data = CreateKeyFrameAnnexB(),
                IsKeyFrame = true
            };

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await sink.WritePacketAsync(packet, CancellationToken.None));
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task Shared_prototype_packet_router_feeds_mp4_and_rtmp()
    {
        var encoder = new TestHardwareVideoEncoder();
        var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 64);

        var mp4Path = Path.Combine(Path.GetTempPath(), $"mf_shared_{Guid.NewGuid():N}.mp4");
        try
        {
            var mp4Sink = new RecordingMp4PacketSink(mp4Path, null, allowPrototypeMuxer: true);
            var rtmpSink = new RtmpPacketSink("rtmp://127.0.0.1/live/shared", allowPrototypeTransport: true);

            router.RegisterConsumer(new RecordingMp4PacketConsumer(mp4Sink));
            router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

            await mp4Sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);
            await rtmpSink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            foreach (var packet in CreateSyntheticH264Packets(30))
                await router.RoutePacketAsync(packet, CancellationToken.None);

            await router.FlushAsync(CancellationToken.None);
            await mp4Sink.StopAsync(CancellationToken.None);
            Assert.True(IsoBmffMp4Writer.HasExperimentalBoxStructure(mp4Path));
            Assert.NotEmpty(rtmpSink.SentPacketsForTests);
            Assert.Same(encoder, router.Encoder);
        }
        finally
        {
            if (File.Exists(mp4Path))
                File.Delete(mp4Path);
        }
    }

    [Fact]
    public async Task Encoded_output_router_does_not_block_fast_consumer_behind_slow_sink()
    {
        var encoder = new TestHardwareVideoEncoder();
        await using var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 2);
        var slow = new BlockingPacketConsumer();
        var fast = new RecordingPacketConsumer();
        router.RegisterConsumer(slow);
        router.RegisterConsumer(fast);

        var start = Environment.TickCount64;
        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - start);

        Assert.True(elapsed < TimeSpan.FromMilliseconds(200));
        await WaitForConditionAsync(() => fast.Packets.Count == 1, TimeSpan.FromSeconds(2));

        slow.Release();
        await router.FlushAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Encoded_output_router_reports_backpressure_when_consumer_queue_is_full()
    {
        var encoder = new TestHardwareVideoEncoder();
        await using var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 1);
        var slow = new BlockingPacketConsumer();
        router.RegisterConsumer(slow);

        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
        await WaitForConditionAsync(() => slow.StartedCount == 1, TimeSpan.FromSeconds(2));
        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None).AsTask().GetAwaiter().GetResult());

        Assert.Contains("backpressure", exception.Message, StringComparison.OrdinalIgnoreCase);
        slow.Release();
    }

    private static IReadOnlyList<EncodedVideoPacket> CreateSyntheticH264Packets(
        int frameCount,
        EncodedVideoPacketEvidence? evidence = null)
    {
        var packets = new List<EncodedVideoPacket>(frameCount);
        for (var index = 0; index < frameCount; index++)
        {
            var isKeyFrame = index % 30 == 0;
            packets.Add(new EncodedVideoPacket
            {
                Codec = EncodedVideoCodec.H264,
                BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
                PresentationTime = TimeSpan.FromMilliseconds(index * 33),
                Duration = TimeSpan.FromMilliseconds(33),
                IsKeyFrame = isKeyFrame,
                Data = isKeyFrame ? CreateKeyFrameAnnexB() : CreatePFrameAnnexB(),
                Evidence = evidence ?? EncodedVideoPacketEvidence.ContractOnly
            });
        }

        return packets;
    }

    private static EncodedPacketSinkContext CreatePacketSinkContext() =>
        new()
        {
            Codec = EncodedVideoCodec.H264,
            Size = new Core.Frames.FrameSize(640, 360),
            FramesPerSecond = 30
        };

    private static byte[] CreateKeyFrameAnnexB() =>
    [
        0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E, 0xAB, 0x40, 0xF0, 0x28, 0xD3, 0x70,
        0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80,
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00, 0x10
    ];

    private static byte[] CreatePFrameAnnexB() =>
    [
        0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24, 0x6C, 0x0F
    ];

    private static int FindBoxTypeOffset(byte[] bytes, string type)
    {
        var pattern = System.Text.Encoding.ASCII.GetBytes(type);
        for (var index = 4; index <= bytes.Length - pattern.Length; index++)
        {
            if (bytes.AsSpan(index, pattern.Length).SequenceEqual(pattern))
                return index;
        }

        throw new InvalidOperationException($"MP4 box '{type}' was not found.");
    }

    private static uint ReadFirstChunkOffset(byte[] bytes)
    {
        var stcoTypeOffset = FindBoxTypeOffset(bytes, "stco");
        var stcoBoxOffset = stcoTypeOffset - 4;
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stcoBoxOffset + 12, 4));
        Assert.Equal(1u, entryCount);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stcoBoxOffset + 16, 4));
    }

    private static bool IsBoxNestedInside(byte[] bytes, string childType, string parentType)
    {
        var childTypeOffset = FindBoxTypeOffset(bytes, childType);
        var childBoxOffset = childTypeOffset - 4;
        var parentTypeOffset = FindBoxTypeOffset(bytes, parentType);
        var parentBoxOffset = parentTypeOffset - 4;
        var parentSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(parentBoxOffset, 4));
        return childBoxOffset >= parentBoxOffset + 8 &&
            childBoxOffset < parentBoxOffset + parentSize;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class TestHardwareVideoEncoder : IHardwareVideoEncoder
    {
        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Test Encoder",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test"
        };

        public HardwareEncoderInputRequirement InputRequirement { get; } = new()
        {
            Width = 640,
            Height = 360,
            PixelFormat = "NV12",
            RequiresGpuSurface = true
        };

        public ValueTask<EncodedVideoPacket?> EncodeAsync(
            Core.Media.Encode.EncodeFrameContext context,
            Core.Media.Audit.IMediaTransportAuditSink auditSink) =>
            ValueTask.FromResult<EncodedVideoPacket?>(CreateSyntheticH264Packets(1).Single());

        public ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
            Core.Gpu.Resources.GpuTextureLease textureLease,
            HardwareEncodeFrameContext context,
            Core.Media.Interop.IGpuFrameExporter frameExporter,
            Core.Media.Audit.IMediaTransportAuditSink auditSink) =>
            EncodeAsync(
                new Core.Media.Encode.EncodeFrameContext
                {
                    InputLease = Core.Media.Interop.HardwareEncoderInputLease.Create(
                        textureLease.ToGpuVideoFrameDescriptor()),
                    FrameNumber = context.FrameId,
                    PresentationTime = context.PresentationTime,
                    CancellationToken = context.CancellationToken
                },
                auditSink);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingPacketConsumer : IEncodedPacketConsumer
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _startedCount;

        public int StartedCount => Volatile.Read(ref _startedCount);

        public async ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startedCount);
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingPacketConsumer : IEncodedPacketConsumer
    {
        private readonly object _gate = new();
        private readonly List<EncodedVideoPacket> _packets = [];

        public IReadOnlyList<EncodedVideoPacket> Packets
        {
            get
            {
                lock (_gate)
                    return _packets.ToArray();
            }
        }

        public ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
        {
            lock (_gate)
                _packets.Add(packet);

            return ValueTask.CompletedTask;
        }
    }
}
