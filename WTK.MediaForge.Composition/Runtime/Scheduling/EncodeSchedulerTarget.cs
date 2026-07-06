using System.Collections.Concurrent;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

/// <summary>
/// Scheduler target for hardware encode. Encode pacing is independent from render pacing.
/// </summary>
internal sealed class EncodeSchedulerTarget : IFrameSchedulerTarget, IAsyncDisposable
{
    private readonly IHardwareVideoEncoder _encoder;
    private readonly IGpuFrameExporter _frameExporter;
    private readonly IMediaTransportAuditSink _auditSink;
    private readonly Func<GpuTextureLease?> _acquireRenderedFrame;
    private readonly Action<EncodedVideoPacket> _onPacketProduced;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly ConcurrentQueue<FrameExecutionContext> _pendingFrames = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _encodeLoop;
    private readonly TimeSpan _encodeTimeout = TimeSpan.FromSeconds(2);
    private int _disposed;

    public EncodeSchedulerTarget(
        IHardwareVideoEncoder encoder,
        IGpuFrameExporter frameExporter,
        IMediaTransportAuditSink auditSink,
        Func<GpuTextureLease?> acquireRenderedFrame,
        Action<EncodedVideoPacket> onPacketProduced,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _frameExporter = frameExporter ?? throw new ArgumentNullException(nameof(frameExporter));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _acquireRenderedFrame = acquireRenderedFrame ?? throw new ArgumentNullException(nameof(acquireRenderedFrame));
        _onPacketProduced = onPacketProduced ?? throw new ArgumentNullException(nameof(onPacketProduced));
        _diagnostics = diagnostics;
        _encodeLoop = Task.Run(ProcessEncodeQueueAsync);
    }

    public int PendingFrameCount => _pendingFrames.Count;

    public void OnScheduledFrame(FrameExecutionContext context)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _pendingFrames.Enqueue(context);
    }

    public async ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _stop.CancelAsync().ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _encodeLoop.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Encode scheduler target did not stop within the expected timeout.", ex);
        }
        finally
        {
            _stop.Dispose();
        }
    }

    public ValueTask DisposeAsync() => StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

    private async Task ProcessEncodeQueueAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (!_pendingFrames.TryDequeue(out var frameContext))
            {
                try
                {
                    await Task.Delay(1, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            using var textureLease = _acquireRenderedFrame();
            if (textureLease is null)
                continue;

            try
            {
                var encodeContext = new HardwareEncodeFrameContext
                {
                    FrameId = frameContext.FrameId,
                    PresentationTime = TimeSpan.FromTicks(frameContext.Timestamp.UtcTicks),
                    FrameBudget = frameContext.FrameBudget,
                    CancellationToken = _stop.Token
                };

                using var timeoutCts = new CancellationTokenSource(_encodeTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, timeoutCts.Token);

                var packet = await _encoder
                    .SubmitFrameAsync(textureLease, encodeContext, _frameExporter, _auditSink)
                    .ConfigureAwait(false);

                if (packet is not null)
                    _onPacketProduced(packet);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encode_scheduler_target_failed",
                    "Encode scheduler target failed to produce an encoded packet.",
                    nameof(EncodeSchedulerTarget),
                    ex);
            }
        }
    }
}
