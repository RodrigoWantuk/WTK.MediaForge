using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Media;

public sealed class EncodedOutputPipelineTests
{
    [Fact]
    public async Task Recording_mp4_public_sink_is_not_enabled_without_prototype_opt_in()
    {
        await using var sink = new RecordingMp4Sink(Path.Combine(Path.GetTempPath(), $"mf_mp4_blocked_{Guid.NewGuid():N}.mp4"));

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sink.StartAsync(
                new RenderOutputSinkContext(
                    Core.Identifiers.RenderOutputId.New(),
                    new Core.Frames.FrameSize(640, 360),
                    Outputs.RenderPixelFormat.Rgba8Unorm,
                    Outputs.RenderBackendKind.Vulkan),
                CancellationToken.None));
    }

    [Fact]
    public async Task Rtmp_public_sink_is_not_enabled_without_prototype_opt_in()
    {
        var sink = new RtmpSink("rtmp://127.0.0.1/live/blocked");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sink.StartAsync(
                new RenderOutputSinkContext(
                    Core.Identifiers.RenderOutputId.New(),
                    new Core.Frames.FrameSize(640, 360),
                    Outputs.RenderPixelFormat.Rgba8Unorm,
                    Outputs.RenderBackendKind.Vulkan),
                CancellationToken.None));
    }

    [Fact]
    public async Task Recording_mp4_prototype_muxer_writes_experimental_file_structure()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_{Guid.NewGuid():N}.mp4");
        try
        {
            var audit = new CollectingMediaTransportAuditSink();
            await using var sink = new RecordingMp4Sink(outputPath, audit, allowPrototypeMuxer: true);
            await sink.StartAsync(
                new RenderOutputSinkContext(
                    Core.Identifiers.RenderOutputId.New(),
                    new Core.Frames.FrameSize(640, 360),
                    Outputs.RenderPixelFormat.Rgba8Unorm,
                    Outputs.RenderBackendKind.Vulkan),
                CancellationToken.None);

            var packets = CreateSyntheticH264Packets(frameCount: 60);
            foreach (var packet in packets)
                await sink.WriteEncodedPacketAsync(packet, CancellationToken.None);

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
        var rtmpSink = new RtmpSink("rtmp://127.0.0.1/live/test", allowPrototypeTransport: true);
        router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

        await rtmpSink.StartAsync(
            new RenderOutputSinkContext(
                Core.Identifiers.RenderOutputId.New(),
                new Core.Frames.FrameSize(640, 360),
                Outputs.RenderPixelFormat.Rgba8Unorm,
                Outputs.RenderBackendKind.Vulkan),
            CancellationToken.None);

        var packet = CreateSyntheticH264Packets(1).Single();
        await router.RoutePacketAsync(packet, CancellationToken.None);

        Assert.NotEmpty(rtmpSink.SentPacketsForTests);
        Assert.Equal(packet.Data, rtmpSink.SentPacketsForTests[0].Data);

        await rtmpSink.DisposeAsync();
        await router.DisposeAsync();
    }

    [Fact]
    public async Task Shared_prototype_packet_router_feeds_mp4_and_rtmp()
    {
        var encoder = new TestHardwareVideoEncoder();
        var router = new EncodedOutputRouter(encoder);

        var mp4Path = Path.Combine(Path.GetTempPath(), $"mf_shared_{Guid.NewGuid():N}.mp4");
        try
        {
            var mp4Sink = new RecordingMp4Sink(mp4Path, null, allowPrototypeMuxer: true);
            var rtmpSink = new RtmpSink("rtmp://127.0.0.1/live/shared", allowPrototypeTransport: true);

            router.RegisterConsumer(new RecordingMp4PacketConsumer(mp4Sink));
            router.RegisterConsumer(new RtmpPacketConsumer(rtmpSink));

            await mp4Sink.StartAsync(
                new RenderOutputSinkContext(
                    Core.Identifiers.RenderOutputId.New(),
                    new Core.Frames.FrameSize(640, 360),
                    Outputs.RenderPixelFormat.Rgba8Unorm,
                    Outputs.RenderBackendKind.Vulkan),
                CancellationToken.None);
            await rtmpSink.StartAsync(
                new RenderOutputSinkContext(
                    Core.Identifiers.RenderOutputId.New(),
                    new Core.Frames.FrameSize(640, 360),
                    Outputs.RenderPixelFormat.Rgba8Unorm,
                    Outputs.RenderBackendKind.Vulkan),
                CancellationToken.None);

            foreach (var packet in CreateSyntheticH264Packets(30))
                await router.RoutePacketAsync(packet, CancellationToken.None);

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

    private static IReadOnlyList<EncodedVideoPacket> CreateSyntheticH264Packets(int frameCount)
    {
        var packets = new List<EncodedVideoPacket>(frameCount);
        for (var index = 0; index < frameCount; index++)
        {
            var isKeyFrame = index % 30 == 0;
            packets.Add(new EncodedVideoPacket
            {
                Codec = EncodedVideoCodec.H264,
                PresentationTime = TimeSpan.FromMilliseconds(index * 33),
                IsKeyFrame = isKeyFrame,
                Data = isKeyFrame ? CreateKeyFrameAnnexB() : CreatePFrameAnnexB()
            });
        }

        return packets;
    }

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
}
