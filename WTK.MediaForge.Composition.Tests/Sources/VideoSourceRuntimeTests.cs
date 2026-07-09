using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Composition.Tests.Gpu;
using WTK.MediaForge.Core.Sources;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class VideoSourceRuntimeTests
{
    [Fact]
    public async Task VideoSourceRuntime_seek_and_loop()
    {
        var path = CreateTempVideoPath();
        try
        {
            using var runtime = CreateRuntime(path, loop: true);
            await runtime.OpenAsync(CancellationToken.None);
            runtime.Seek(TimeSpan.FromSeconds(2));
            Assert.Equal(TimeSpan.FromSeconds(2), runtime.Clock.CurrentPresentationTime);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task VideoSourceRuntime_pause_preserves_clock()
    {
        var path = CreateTempVideoPath();
        try
        {
            using var runtime = CreateRuntime(path, loop: false);
            await runtime.OpenAsync(CancellationToken.None);
            runtime.Play();
            runtime.Seek(TimeSpan.FromSeconds(1));
            runtime.Pause();
            var pausedAt = runtime.Clock.CurrentPresentationTime;
            await Task.Delay(20);
            Assert.Equal(pausedAt, runtime.Clock.CurrentPresentationTime);
            Assert.Equal(MediaSourceState.Paused, runtime.State);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task VideoSourceRuntime_decodes_file_frames_without_empty_packet()
    {
        var path = CreateTempVideoPath();
        try
        {
            var decoder = new RecordingFileDecoder();
            using var runtime = new VideoSourceRuntime(
                new VideoFileSourceSettings { Path = path },
                _ => decoder,
                diagnostics: null);

            await runtime.OpenAsync(CancellationToken.None);
            runtime.Play();
            runtime.Seek(TimeSpan.FromMilliseconds(250));

            await runtime.TryDecodeNextFrameAsync(new CollectingMediaTransportAuditSink(), CancellationToken.None);

            Assert.Single(decoder.FileDecodeContexts);
            Assert.Equal(TimeSpan.FromMilliseconds(250), decoder.FileDecodeContexts[0].PresentationTime);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task VideoSourceRuntime_decode_and_queue_enqueues_decoded_texture_lease()
    {
        var path = CreateTempVideoPath();
        try
        {
            var decoder = new QueuedFrameDecoder();
            using var runtime = new VideoSourceRuntime(
                new VideoFileSourceSettings { Path = path },
                _ => decoder,
                diagnostics: null);

            await runtime.OpenAsync(CancellationToken.None);
            runtime.Play();

            var queued = await runtime.DecodeAndQueueNextFrameAsync(
                new CollectingMediaTransportAuditSink(),
                CancellationToken.None);

            Assert.True(queued);
            Assert.True(runtime.StreamQueue.TryAcquire(out var lease));
            Assert.NotNull(lease);
            Assert.Equal(decoder.DecodedTextureId, lease!.TextureId);

            lease.Dispose();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Dispose_flushes_decoder_and_releases_queued_frame()
    {
        var path = CreateTempVideoPath();
        try
        {
            var decoder = new QueuedFrameDecoder();
            var runtime = new VideoSourceRuntime(
                new VideoFileSourceSettings { Path = path },
                _ => decoder,
                diagnostics: null);

            await runtime.OpenAsync(CancellationToken.None);
            await runtime.DecodeAndQueueNextFrameAsync(
                new CollectingMediaTransportAuditSink(),
                CancellationToken.None);

            Assert.Equal(1, runtime.StreamQueue.Count);

            runtime.Dispose();

            Assert.True(decoder.FlushCalled);
            Assert.True(decoder.DisposeCalled);
            Assert.Equal(0, runtime.StreamQueue.Count);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Decode_failure_surfaces_diagnostic_without_crash()
    {
        using var runtime = new VideoSourceRuntime(
            new VideoFileSourceSettings { Path = string.Empty },
            _ => new ThrowingDecoder(),
            diagnostics: null);

        await Assert.ThrowsAsync<FileNotFoundException>(() => runtime.OpenAsync(CancellationToken.None));
    }

    private static string CreateTempVideoPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-source-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        return path;
    }

    private static VideoSourceRuntime CreateRuntime(string path, bool loop) =>
        new(
            new VideoFileSourceSettings { Path = path, Loop = loop },
            _ => new StubDecoder(),
            diagnostics: null);

    private sealed class StubDecoder : IHardwareFileVideoDecoder
    {
        public HardwareDecoderInfo Info { get; } = new()
        {
            Name = "Stub",
            Codec = EncodedVideoCodec.H264,
            Backend = "Stub",
            ProducesGpuSurface = true
        };

        public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink)
        {
            _ = context;
            _ = auditSink;
            return ValueTask.CompletedTask;
        }

        public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
            FileDecodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            _ = context;
            _ = auditSink;
            return ValueTask.FromResult<DecodedGpuFrame?>(null);
        }

        public ValueTask FlushAsync(IMediaTransportAuditSink auditSink)
        {
            _ = auditSink;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingDecoder : IHardwareFileVideoDecoder
    {
        public HardwareDecoderInfo Info { get; } = new()
        {
            Name = "Throwing",
            Codec = EncodedVideoCodec.H264,
            Backend = "Throwing"
        };

        public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink) =>
            throw new FileNotFoundException("missing", context.SourcePath);

        public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
            FileDecodeFrameContext context,
            IMediaTransportAuditSink auditSink) =>
            ValueTask.FromResult<DecodedGpuFrame?>(null);

        public ValueTask FlushAsync(IMediaTransportAuditSink auditSink) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFileDecoder : IHardwareFileVideoDecoder
    {
        public List<FileDecodeFrameContext> FileDecodeContexts { get; } = [];

        public HardwareDecoderInfo Info { get; } = new()
        {
            Name = "Recording",
            Codec = EncodedVideoCodec.H264,
            Backend = "Recording",
            ProducesGpuSurface = true
        };

        public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink) =>
            ValueTask.CompletedTask;

        public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
            FileDecodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            FileDecodeContexts.Add(context);
            return ValueTask.FromResult<DecodedGpuFrame?>(null);
        }

        public ValueTask FlushAsync(IMediaTransportAuditSink auditSink) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class QueuedFrameDecoder : IHardwareFileVideoDecoder
    {
        private readonly GpuResourcePool _pool = new(new FakeTextureFactory());

        public GpuTextureId DecodedTextureId { get; private set; }

        public bool FlushCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public HardwareDecoderInfo Info { get; } = new()
        {
            Name = "Queued",
            Codec = EncodedVideoCodec.H264,
            Backend = "Queued",
            ProducesGpuSurface = true
        };

        public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink) =>
            ValueTask.CompletedTask;

        public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
            FileDecodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            var lease = _pool.AcquireTexture(new GpuTextureDescriptor
            {
                Width = 64,
                Height = 64,
                Usage = GpuTextureUsage.OffscreenColor
            });
            DecodedTextureId = lease.TextureId;

            return ValueTask.FromResult<DecodedGpuFrame?>(
                new DecodedGpuFrame(
                    lease,
                    context.PresentationTime,
                    TimeSpan.FromMilliseconds(33)));
        }

        public ValueTask FlushAsync(IMediaTransportAuditSink auditSink)
        {
            FlushCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            _pool.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
