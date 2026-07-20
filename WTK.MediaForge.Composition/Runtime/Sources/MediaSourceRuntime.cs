using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class MediaSourceRuntime : IDisposable, IAsyncDisposable
{
    private readonly SourceFrameBuffer _buffer;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private int _disposed;

    public MediaSourceRuntime(
        IVideoFrameProvider provider,
        MediaSourceTypeId typeId = default,
        MediaSourceCapabilities? capabilities = null,
        MediaSourceBufferOptions? bufferOptions = null,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        TypeId = typeId;
        Capabilities = capabilities ?? MediaSourceCapabilities.LiveGpuVideo;
        _buffer = new SourceFrameBuffer(bufferOptions);
        _diagnostics = diagnostics;
    }

    public SourceId SourceId => Provider.Id;

    public string Name => Provider.Name;

    public MediaSourceTypeId TypeId { get; }

    public MediaSourceCapabilities Capabilities { get; }

    public MediaSourceState State => Provider.State;

    public Exception? LastError => Provider.LastError;

    public IVideoFrameProvider Provider { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Provider.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Provider.StopAsync(cancellationToken);

    public SourceFrameAcquireResult TryAcquireFrameForRender(TimeSpan renderTimestamp)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        GpuFrameLease? providerLease = null;

        try
        {
            if (Provider.State == MediaSourceState.Failed)
            {
                var failure = Provider.LastError ?? new InvalidOperationException(
                    $"Source provider '{Name}' entered Failed state without an exception.");
                ReportAcquireFailure(failure);
                return SourceFrameAcquireResult.SourceFailed(failure);
            }

            if (Provider.TryAcquireLatestFrame(out providerLease!))
            {
                _buffer.Publish(providerLease);
                providerLease = null;
            }

            return _buffer.TryAcquireForRender(renderTimestamp, out var bufferedLease)
                ? SourceFrameAcquireResult.Acquired(bufferedLease)
                : SourceFrameAcquireResult.NoFrameAvailable();
        }
        catch (Exception ex)
        {
            ReportAcquireFailure(ex);

            return SourceFrameAcquireResult.SourceFailed(ex);
        }
        finally
        {
            providerLease?.Dispose();
        }
    }

    private void ReportAcquireFailure(Exception exception) =>
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Error,
            "source.frame_acquire_failed",
            $"Source '{Name}' failed while acquiring a frame for render.",
            nameof(MediaSourceRuntime),
            exception,
            SourceId.Value,
            Name);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _buffer.Dispose();
        if (Provider is IDisposable disposableProvider)
            disposableProvider.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _buffer.Dispose();
        if (Provider is IAsyncDisposable asyncDisposableProvider)
        {
            await asyncDisposableProvider.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (Provider is IDisposable disposableProvider)
            disposableProvider.Dispose();
    }
}
