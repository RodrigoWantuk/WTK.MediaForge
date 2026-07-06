using WTK.MediaForge.Composition.Runtime.Streaming;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Sources;

internal sealed class VideoSourceRuntime : IDisposable
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly VideoClock _clock = new();
    private IHardwareVideoDecoder? _decoder;
    private bool _disposed;

    public VideoSourceRuntime(
        VideoFileSourceSettings settings,
        Func<HardwareDecodeOpenContext, IHardwareVideoDecoder> decoderFactory,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        DecoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _diagnostics = diagnostics;
        StreamQueue = new TextureLeaseQueue(capacity: 3, TextureLeaseQueuePolicy.KeepLatest);
    }

    public VideoFileSourceSettings Settings { get; }

    public Func<HardwareDecodeOpenContext, IHardwareVideoDecoder> DecoderFactory { get; }

    public TextureLeaseQueue StreamQueue { get; }

    public IVideoClock Clock => _clock;

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_decoder is not null)
            return;

        _decoder = DecoderFactory(new HardwareDecodeOpenContext
        {
            SourcePath = Settings.Path,
            Session = new HardwareDecodeSession
            {
                Codec = EncodedVideoCodec.H264,
                Width = 0,
                Height = 0,
                PreferHardware = true
            },
            CancellationToken = cancellationToken
        });

        var audit = new CollectingMediaTransportAuditSink();
        await _decoder.OpenAsync(
            new HardwareDecodeOpenContext
            {
                SourcePath = Settings.Path,
                Session = new HardwareDecodeSession
                {
                    Codec = EncodedVideoCodec.H264,
                    Width = 0,
                    Height = 0,
                    PreferHardware = true
                },
                CancellationToken = cancellationToken
            },
            audit);

        State = MediaSourceState.Stopped;
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Play();
        State = MediaSourceState.Running;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Pause();
        if (State == MediaSourceState.Running)
            State = (MediaSourceState)3; // Paused
    }

    public void Seek(TimeSpan presentationTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Seek(presentationTime);
    }

    public async ValueTask<DecodedGpuFrame?> TryDecodeNextFrameAsync(
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(auditSink);

        if (_decoder is null)
            return null;

        var packet = new EncodedVideoPacket
        {
            Data = ReadOnlyMemory<byte>.Empty,
            Codec = EncodedVideoCodec.H264,
            PresentationTime = _clock.CurrentPresentationTime
        };

        var frame = await _decoder.DecodeAsync(
            new DecodeFrameContext
            {
                Packet = packet,
                FrameNumber = 1,
                PresentationTime = _clock.CurrentPresentationTime,
                CancellationToken = cancellationToken
            },
            auditSink);

        if (frame is null)
            return null;

        return frame;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Pause();

        if (_decoder is not null)
        {
            await _decoder.FlushAsync(new CollectingMediaTransportAuditSink());
            await _decoder.DisposeAsync();
            _decoder = null;
        }

        StreamQueue.Clear();
        State = MediaSourceState.Stopped;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = StopAsync();
        StreamQueue.Dispose();
    }
}
