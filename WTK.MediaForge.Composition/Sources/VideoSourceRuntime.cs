using System.Runtime.ExceptionServices;
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
    private IHardwareFileVideoDecoder? _decoder;
    private long _frameNumber;
    private bool _disposed;

    public VideoSourceRuntime(
        VideoFileSourceSettings settings,
        Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder> decoderFactory,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        DecoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _diagnostics = diagnostics;
        StreamQueue = new TextureLeaseQueue(capacity: 3, TextureLeaseQueuePolicy.KeepLatest);
    }

    public VideoFileSourceSettings Settings { get; }

    public Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder> DecoderFactory { get; }

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
            State = MediaSourceState.Paused;
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

        var frame = await _decoder.DecodeNextFrameAsync(
            new FileDecodeFrameContext
            {
                FrameNumber = Interlocked.Increment(ref _frameNumber),
                PresentationTime = _clock.CurrentPresentationTime,
                CancellationToken = cancellationToken
            },
            auditSink);

        if (frame is null)
            return null;

        return frame;
    }

    public async ValueTask<bool> DecodeAndQueueNextFrameAsync(
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        using var frame = await TryDecodeNextFrameAsync(auditSink, cancellationToken);
        if (frame is null)
            return false;

        var lease = frame.TakeTextureLease();
        try
        {
            StreamQueue.Enqueue(lease);
            return true;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clock.Pause();

        Exception? cleanupFailure = null;

        if (_decoder is not null)
        {
            var decoder = _decoder;
            _decoder = null;

            try
            {
                await decoder.FlushAsync(new CollectingMediaTransportAuditSink())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            StreamQueue.Clear();

            try
            {
                await decoder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = cleanupFailure is null
                    ? ex
                    : new AggregateException(cleanupFailure, ex);
            }
        }
        else
        {
            StreamQueue.Clear();
        }

        State = MediaSourceState.Stopped;

        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            StopCoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            StreamQueue.Dispose();
        }
    }
}
