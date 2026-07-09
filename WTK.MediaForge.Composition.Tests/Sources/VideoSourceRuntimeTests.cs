using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
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
}
