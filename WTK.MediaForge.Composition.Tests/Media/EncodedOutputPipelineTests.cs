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
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WTK.MediaForge.Composition.Tests.Media;

public sealed class EncodedOutputPipelineTests
{
    [Theory]
    [InlineData(0L, 8)]
    [InlineData(4_294_967_287L, 8)]
    [InlineData(4_294_967_288L, 16)]
    [InlineData(30_000_000_000L, 16)]
    public void Mp4_mdat_uses_extended_size_for_long_recordings(long payloadLength, int expectedHeaderSize)
    {
        Assert.Equal(expectedHeaderSize, IsoBmffMp4Writer.GetMdatHeaderSize(payloadLength));
    }

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
    public async Task Rtmp_public_sink_connects_publishes_and_sends_flv_video_over_tcp()
    {
        await using var server = new FakeRtmpServer();
        await using var sink = new RtmpSink(server.Url);

        await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);
        await sink.WritePacketAsync(
            CreateSyntheticH264Packets(1, CreateBackendValidatedRtmpEvidence()).Single(),
            CancellationToken.None);
        await server.WaitForVideoPacketsAsync(2, TimeSpan.FromSeconds(5));

        Assert.Contains("connect", server.CommandNames);
        Assert.Contains("createStream", server.CommandNames);
        Assert.Contains("publish", server.CommandNames);
        Assert.Equal(2, server.VideoPacketPayloads.Count);
        Assert.Equal(0x17, server.VideoPacketPayloads[0][0]);
        Assert.Equal(0, server.VideoPacketPayloads[0][1]);
        Assert.Equal(0x17, server.VideoPacketPayloads[1][0]);
        Assert.Equal(1, server.VideoPacketPayloads[1][1]);
    }

    [Fact]
    public async Task Rtmp_public_sink_rejects_packets_without_backend_validation()
    {
        await using var server = new FakeRtmpServer();
        await using var sink = new RtmpSink(server.Url);

        await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sink.WritePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None));

        Assert.Contains("BackendOutputValidated", exception.Message, StringComparison.Ordinal);
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
    public async Task Recording_mp4_product_muxer_writes_valid_file_structure()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_{Guid.NewGuid():N}.mp4");
        try
        {
            var audit = new CollectingMediaTransportAuditSink();
            await using var sink = new RecordingMp4Sink(outputPath, audit);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var packets = CreateSyntheticH264Packets(frameCount: 60, CreateBackendValidatedMp4Evidence());
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
        var transport = new RecordingRtmpTransport();
        var rtmpSink = new RtmpPacketSink("rtmp://127.0.0.1/live/test", () => transport);
        router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

        await rtmpSink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

        var packet = CreateSyntheticH264Packets(1, CreateBackendValidatedRtmpEvidence()).Single();
        await router.RoutePacketAsync(packet, CancellationToken.None);
        await router.FlushAsync(CancellationToken.None);

        Assert.Equal(2, transport.SentPackets.Count);
        Assert.True(transport.SentPackets[0].IsCodecConfiguration);
        Assert.Equal(EncodedVideoBitstreamFormat.Avcc, transport.SentPackets[0].BitstreamFormat);
        Assert.Equal(0x17, transport.SentPackets[0].Data.Span[0]);
        Assert.Equal(0, transport.SentPackets[0].Data.Span[1]);
        Assert.False(transport.SentPackets[1].IsCodecConfiguration);
        Assert.Equal(EncodedVideoBitstreamFormat.Avcc, transport.SentPackets[1].BitstreamFormat);
        Assert.Equal(0x17, transport.SentPackets[1].Data.Span[0]);
        Assert.Equal(1, transport.SentPackets[1].Data.Span[1]);

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
    public async Task Recording_mp4_product_muxer_writes_avcc_packet_with_codec_configuration()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_avcc_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4Sink(outputPath);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            await sink.WritePacketAsync(
                new EncodedVideoPacket
                {
                    Codec = EncodedVideoCodec.H264,
                    BitstreamFormat = EncodedVideoBitstreamFormat.Avcc,
                    PresentationTime = TimeSpan.Zero,
                    Duration = TimeSpan.FromMilliseconds(33),
                    Data = CreateKeyFrameAvcc(),
                    CodecConfiguration = CreateAvcCConfiguration(),
                    IsKeyFrame = true,
                    Evidence = CreateBackendValidatedMp4Evidence()
                },
                CancellationToken.None);

            await sink.StopAsync(CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(IsoBmffMp4Writer.HasValidH264BoxStructure(
                outputPath,
                new IsoBmffMp4Writer.TrackMetadata(640, 360),
                minimumSampleCount: 1));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Recording_mp4_product_muxer_rejects_avcc_packet_without_codec_configuration()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_avcc_no_config_{Guid.NewGuid():N}.mp4");
        try
        {
            await using var sink = new RecordingMp4Sink(outputPath);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await sink.WritePacketAsync(
                    new EncodedVideoPacket
                    {
                        Codec = EncodedVideoCodec.H264,
                        BitstreamFormat = EncodedVideoBitstreamFormat.Avcc,
                        PresentationTime = TimeSpan.Zero,
                        Duration = TimeSpan.FromMilliseconds(33),
                        Data = CreateKeyFrameAvcc(),
                        IsKeyFrame = true,
                        Evidence = CreateBackendValidatedMp4Evidence()
                    },
                    CancellationToken.None));

            Assert.Contains("codec configuration", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            await using var sink = new RecordingMp4PacketSink(outputPath);
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
            var sink = new RecordingMp4PacketSink(outputPath);
            await sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);
            await sink.WritePacketAsync(
                CreateSyntheticH264Packets(1, CreateBackendValidatedMp4Evidence()).Single(),
                CancellationToken.None);
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
                    IsKeyFrame = true,
                    Evidence = CreateBackendValidatedMp4Evidence()
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
        var sink = new RtmpPacketSink("rtmp://127.0.0.1/live/unknown", () => new RecordingRtmpTransport());
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
    public async Task Shared_validated_packet_router_feeds_mp4_and_rtmp()
    {
        var encoder = new TestHardwareVideoEncoder();
        var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 64);

        var mp4Path = Path.Combine(Path.GetTempPath(), $"mf_shared_{Guid.NewGuid():N}.mp4");
        try
        {
            var transport = new RecordingRtmpTransport();
            var mp4Sink = new RecordingMp4PacketSink(mp4Path);
            var rtmpSink = new RtmpPacketSink("rtmp://127.0.0.1/live/shared", () => transport);

            router.RegisterConsumer(new RecordingMp4PacketConsumer(mp4Sink));
            router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

            await mp4Sink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);
            await rtmpSink.StartAsync(CreatePacketSinkContext(), CancellationToken.None);

            foreach (var packet in CreateSyntheticH264Packets(30, CreateBackendValidatedMp4Evidence()))
                await router.RoutePacketAsync(packet, CancellationToken.None);

            await router.FlushAsync(CancellationToken.None);
            await mp4Sink.StopAsync(CancellationToken.None);
            Assert.True(IsoBmffMp4Writer.HasValidH264BoxStructure(
                mp4Path,
                new IsoBmffMp4Writer.TrackMetadata(640, 360),
                minimumSampleCount: 30));
            Assert.NotEmpty(transport.SentPackets);
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
        try
        {
            await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
            await WaitForConditionAsync(() => slow.StartedCount == 1, TimeSpan.FromSeconds(2));
            await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);

            await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
            var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
                await router.FlushAsync(CancellationToken.None));

            Assert.Contains(
                exception.InnerExceptions,
                inner => inner.GetBaseException().Message.Contains(
                    "backpressure",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            slow.Release();
        }
    }

    [Fact]
    public async Task Encoded_output_router_drop_policy_isolates_slow_consumer_from_fast_consumer()
    {
        var encoder = new TestHardwareVideoEncoder();
        await using var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 1);
        var slow = new BlockingPacketConsumer();
        var fast = new RecordingPacketConsumer();
        router.RegisterConsumer(slow, new EncodedPacketConsumerOptions
        {
            BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.DropOutput,
            DisplayName = "slow-drop"
        });
        router.RegisterConsumer(fast);

        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
        await WaitForConditionAsync(() => slow.StartedCount == 1, TimeSpan.FromSeconds(2));

        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
        await WaitForConditionAsync(() => fast.Packets.Count == 2, TimeSpan.FromSeconds(2));
        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);

        await WaitForConditionAsync(() => fast.Packets.Count == 3, TimeSpan.FromSeconds(2));
        slow.Release();
    }

    [Fact]
    public async Task Encoded_output_router_rejects_drop_policies_for_lossless_outputs()
    {
        var encoder = new TestHardwareVideoEncoder();
        await using var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 1);

        var dropException = Assert.Throws<ArgumentException>(() =>
            router.RegisterConsumer(new RecordingPacketConsumer(), new EncodedPacketConsumerOptions
            {
                RequiresLosslessDelivery = true,
                BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.DropOutput,
                DisplayName = "recording"
            }));

        var keepLatestException = Assert.Throws<ArgumentException>(() =>
            router.RegisterConsumer(new RecordingPacketConsumer(), new EncodedPacketConsumerOptions
            {
                RequiresLosslessDelivery = true,
                BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.KeepLatest,
                DisplayName = "stream"
            }));

        Assert.Contains("Lossless encoded outputs", dropException.Message, StringComparison.Ordinal);
        Assert.Contains("Lossless encoded outputs", keepLatestException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Encoded_output_router_reports_consumer_statistics()
    {
        var encoder = new TestHardwareVideoEncoder();
        await using var router = new EncodedOutputRouter(encoder, consumerQueueCapacity: 2);
        var consumer = new RecordingPacketConsumer();
        router.RegisterConsumer(consumer, new EncodedPacketConsumerOptions
        {
            BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.Backpressure,
            DisplayName = "mp4"
        });

        await router.RoutePacketAsync(CreateSyntheticH264Packets(1).Single(), CancellationToken.None);
        await WaitForConditionAsync(() => consumer.Packets.Count == 1, TimeSpan.FromSeconds(2));

        var stats = Assert.Single(router.GetConsumerStatistics());
        Assert.Equal("mp4", stats.DisplayName);
        Assert.Equal(EncodedPacketConsumerBackpressurePolicy.Backpressure, stats.BackpressurePolicy);
        Assert.Equal(1, stats.EnqueuedPackets);
        Assert.Equal(1, stats.WrittenPackets);
        Assert.Equal(0, stats.DroppedPackets);
        Assert.Equal(0, stats.FailedWrites);
        Assert.Equal(0, stats.TimedOutWrites);
        Assert.Null(stats.LastError);
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

    private static byte[] CreateKeyFrameAvcc() =>
    [
        0x00, 0x00, 0x00, 0x05, 0x65, 0x88, 0x84, 0x00, 0x10
    ];

    private static byte[] CreateAvcCConfiguration() =>
    [
        0x01, 0x42, 0x00, 0x1E, 0xFF, 0xE1,
        0x00, 0x09, 0x67, 0x42, 0x00, 0x1E, 0xAB, 0x40, 0xF0, 0x28, 0xD3,
        0x01, 0x00, 0x04, 0x68, 0xCE, 0x3C, 0x80
    ];

    private static EncodedVideoPacketEvidence CreateBackendValidatedMp4Evidence() =>
        EncodedVideoPacketEvidence.CreateBackendOutputValidated(
            nameof(EncodedOutputPipelineTests),
            "TestBackend",
            MediaForgeCapabilityCatalog.Mp4RecordingProof);

    private static EncodedVideoPacketEvidence CreateBackendValidatedRtmpEvidence() =>
        EncodedVideoPacketEvidence.CreateBackendOutputValidated(
            nameof(EncodedOutputPipelineTests),
            "TestBackend",
            MediaForgeCapabilityCatalog.RtmpNetworkOutputProof);

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

        if (condition())
            return;

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class RecordingRtmpTransport : IRtmpTransport
    {
        public string Url => "rtmp://127.0.0.1/live/test";

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
            if (!IsConnected)
                throw new InvalidOperationException("Transport is not connected.");
            SentPackets.Add(packet);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => IsConnected = false;
    }

    private sealed class FakeRtmpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serverTask;
        private readonly object _gate = new();
        private readonly List<string> _commandNames = [];
        private readonly List<byte[]> _videoPacketPayloads = [];
        private TcpClient? _client;
        private Exception? _failure;
        private int _chunkSize = 128;

        public FakeRtmpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = $"rtmp://127.0.0.1:{endpoint.Port}/live/test";
            _serverTask = Task.Run(RunAsync);
        }

        public string Url { get; }

        public IReadOnlyList<string> CommandNames
        {
            get
            {
                lock (_gate)
                    return _commandNames.ToArray();
            }
        }

        public IReadOnlyList<byte[]> VideoPacketPayloads
        {
            get
            {
                lock (_gate)
                    return _videoPacketPayloads.ToArray();
            }
        }

        public async Task WaitForVideoPacketsAsync(int count, TimeSpan timeout)
        {
            await WaitForConditionAsync(
                    () =>
                    {
                        ThrowIfFailed();
                        lock (_gate)
                            return _videoPacketPayloads.Count >= count;
                    },
                    timeout)
                .ConfigureAwait(false);
            ThrowIfFailed();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            _client?.Dispose();

            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }

            _stop.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                await using var stream = _client.GetStream();
                await PerformHandshakeAsync(stream, _stop.Token).ConfigureAwait(false);

                while (!_stop.IsCancellationRequested)
                {
                    var message = await ReadMessageAsync(stream, _stop.Token).ConfigureAwait(false);
                    if (message is null)
                        return;

                    if (message.Value.MessageTypeId == 1 && message.Value.Payload.Length == 4)
                    {
                        _chunkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(message.Value.Payload);
                    }
                    else if (message.Value.MessageTypeId == 20)
                    {
                        CaptureCommandNames(message.Value.Payload);
                        if (PayloadContains(message.Value.Payload, "connect"))
                            await WriteCommandResponseAsync(stream, transactionId: 1, numericResult: null, _stop.Token).ConfigureAwait(false);
                        else if (PayloadContains(message.Value.Payload, "createStream"))
                            await WriteCommandResponseAsync(stream, transactionId: 2, numericResult: 1, _stop.Token).ConfigureAwait(false);
                    }
                    else if (message.Value.MessageTypeId == 9)
                    {
                        lock (_gate)
                            _videoPacketPayloads.Add(message.Value.Payload);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_stop.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
        }

        private static async ValueTask PerformHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var c0c1 = new byte[1 + 1536];
            await stream.ReadExactlyAsync(c0c1, cancellationToken).ConfigureAwait(false);
            Assert.Equal(3, c0c1[0]);

            var s0s1s2 = new byte[1 + 1536 + 1536];
            s0s1s2[0] = 3;
            BinaryPrimitives.WriteUInt32BigEndian(s0s1s2.AsSpan(1, 4), (uint)Environment.TickCount);
            c0c1.AsSpan(1, 1536).CopyTo(s0s1s2.AsSpan(1 + 1536));
            await stream.WriteAsync(s0s1s2, cancellationToken).ConfigureAwait(false);

            var c2 = new byte[1536];
            await stream.ReadExactlyAsync(c2, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<RtmpMessage?> ReadMessageAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var firstByte = new byte[1];
            var bytesRead = await stream.ReadAsync(firstByte, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
                return null;

            var fmt = firstByte[0] >> 6;
            if (fmt != 0)
                throw new InvalidOperationException($"Fake RTMP server expected a full chunk header, got fmt={fmt}.");

            var header = new byte[11];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var timestamp = ReadUInt24BigEndian(header.AsSpan(0, 3));
            var length = (int)ReadUInt24BigEndian(header.AsSpan(3, 3));
            var messageTypeId = header[6];
            if (timestamp == 0xFFFFFF)
            {
                var extendedTimestamp = new byte[4];
                await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
            }

            var payload = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var count = Math.Min(_chunkSize, length - offset);
                await stream.ReadExactlyAsync(payload.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                offset += count;

                if (offset < length)
                {
                    var continuationHeader = new byte[1];
                    await stream.ReadExactlyAsync(continuationHeader, cancellationToken).ConfigureAwait(false);
                    if (timestamp == 0xFFFFFF)
                    {
                        var extendedTimestamp = new byte[4];
                        await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return new RtmpMessage(messageTypeId, payload);
        }

        private void CaptureCommandNames(byte[] payload)
        {
            var text = Encoding.UTF8.GetString(payload);
            lock (_gate)
            {
                if (text.Contains("connect", StringComparison.Ordinal))
                    _commandNames.Add("connect");
                if (text.Contains("createStream", StringComparison.Ordinal))
                    _commandNames.Add("createStream");
                if (text.Contains("publish", StringComparison.Ordinal))
                    _commandNames.Add("publish");
            }
        }

        private static bool PayloadContains(byte[] payload, string value) =>
            Encoding.UTF8.GetString(payload).Contains(value, StringComparison.Ordinal);

        private static async ValueTask WriteCommandResponseAsync(
            NetworkStream stream,
            double transactionId,
            double? numericResult,
            CancellationToken cancellationToken)
        {
            using var payload = new MemoryStream();
            WriteAmfString(payload, "_result");
            WriteAmfNumber(payload, transactionId);
            WriteAmfNull(payload);
            if (numericResult.HasValue)
                WriteAmfNumber(payload, numericResult.Value);
            else
                WriteAmfNull(payload);

            var data = payload.ToArray();
            var header = new byte[12];
            header[0] = 3;
            WriteUInt24BigEndian(header.AsSpan(1, 3), 0);
            WriteUInt24BigEndian(header.AsSpan(4, 3), (uint)data.Length);
            header[7] = 20;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 0);

            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        private static void WriteAmfString(Stream output, string value)
        {
            output.WriteByte(0x02);
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
            output.Write(length);
            output.Write(bytes);
        }

        private static void WriteAmfNumber(Stream output, double value)
        {
            output.WriteByte(0x00);
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            output.Write(bytes);
        }

        private static void WriteAmfNull(Stream output) => output.WriteByte(0x05);

        private void ThrowIfFailed()
        {
            if (_failure is not null)
                throw new InvalidOperationException("Fake RTMP server failed.", _failure);
        }

        private static uint ReadUInt24BigEndian(ReadOnlySpan<byte> source) =>
            ((uint)source[0] << 16) | ((uint)source[1] << 8) | source[2];

        private static void WriteUInt24BigEndian(Span<byte> destination, uint value)
        {
            destination[0] = (byte)(value >> 16);
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)value;
        }

        private readonly record struct RtmpMessage(byte MessageTypeId, byte[] Payload);
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

        public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
            Core.Media.Audit.IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>([]);

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
