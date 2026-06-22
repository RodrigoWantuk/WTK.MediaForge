using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class SourceRuntimeManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<SourceId, MediaSourceRuntime> _runtimes = [];
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

    public SourceRuntimeManager(IMediaForgeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics;

    public int Count
    {
        get
        {
            lock (_gate)
                return _runtimes.Count;
        }
    }

    public MediaSourceRuntime RegisterProvider(
        IVideoFrameProvider provider,
        MediaSourceTypeId typeId = default,
        MediaSourceBufferOptions? bufferOptions = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Id.IsEmpty)
            throw new ArgumentException("Provider SourceId cannot be empty.", nameof(provider));

        var runtime = new MediaSourceRuntime(
            provider,
            typeId,
            ResolveCapabilities(typeId),
            ResolveBufferOptions(typeId, bufferOptions),
            _diagnostics);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_runtimes.TryGetValue(provider.Id, out var previous))
                previous.Dispose();

            _runtimes[provider.Id] = runtime;
        }

        return runtime;
    }

    public MediaSourceRuntime RegisterProvider(
        IVideoFrameProvider provider,
        MediaForgeSourceDefinition sourceDefinition,
        MediaSourceBufferOptions? bufferOptions = null)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        return RegisterProvider(provider, sourceDefinition.TypeId, bufferOptions);
    }

    public void UnregisterProvider(SourceId sourceId)
    {
        MediaSourceRuntime? runtime = null;

        lock (_gate)
        {
            if (_runtimes.Remove(sourceId, out var removed))
                runtime = removed;
        }

        runtime?.Dispose();
    }

    public bool TryGetRuntime(SourceId sourceId, out MediaSourceRuntime runtime)
    {
        lock (_gate)
            return _runtimes.TryGetValue(sourceId, out runtime!);
    }

    public bool TryGetProvider(SourceId sourceId, out IVideoFrameProvider provider)
    {
        if (TryGetRuntime(sourceId, out var runtime))
        {
            provider = runtime.Provider;
            return true;
        }

        provider = null!;
        return false;
    }

    public SourceFrameAcquireResult TryAcquireFrame(SourceId sourceId, TimeSpan renderTimestamp) =>
        TryGetRuntime(sourceId, out var runtime)
            ? runtime.TryAcquireFrameForRender(renderTimestamp)
            : SourceFrameAcquireResult.SourceNotRegistered();

    public async Task StartAllAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Source start timeout must be positive.");

        List<MediaSourceRuntime> started = [];

        try
        {
            foreach (var runtime in SnapshotRuntimes())
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                var startTask = runtime.StartAsync(timeoutCts.Token);
                await startTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                started.Add(runtime);
            }
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.start_failed",
                "Failed to start one or more source runtimes.",
                nameof(SourceRuntimeManager),
                ex);

            await StopStartedAfterStartFailureAsync(started, cancellationToken).ConfigureAwait(false);

            if (ex is TimeoutException ||
                (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new TimeoutException("One or more source runtimes did not start before the timeout.", ex);
            }

            throw;
        }
    }

    public async Task StartAllAsync(CancellationToken cancellationToken)
    {
        List<MediaSourceRuntime> started = [];

        try
        {
            foreach (var runtime in SnapshotRuntimes())
            {
                await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
                started.Add(runtime);
            }
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.start_failed",
                "Failed to start one or more source runtimes.",
                nameof(SourceRuntimeManager),
                ex);

            await StopStartedAfterStartFailureAsync(started, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        await StopAllAsync(
            static (runtime, ct) => runtime.StopAsync(ct),
            onFailure: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllAsync(
        Func<MediaSourceRuntime, CancellationToken, Task> stopRuntimeAsync,
        Action<MediaSourceRuntime, Exception>? onFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopRuntimeAsync);
        cancellationToken.ThrowIfCancellationRequested();

        List<Exception>? errors = null;

        foreach (var runtime in SnapshotRuntimes().AsEnumerable().Reverse())
        {
            try
            {
                await stopRuntimeAsync(runtime, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.stop_failed",
                    $"Source '{runtime.Name}' failed to stop.",
                    nameof(SourceRuntimeManager),
                    ex);
                onFailure?.Invoke(runtime, ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to stop one or more source runtimes.", errors);
    }

    public void Clear()
    {
        List<MediaSourceRuntime> runtimes;

        lock (_gate)
        {
            runtimes = _runtimes.Values.ToList();
            _runtimes.Clear();
        }

        foreach (var runtime in runtimes)
            runtime.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        Clear();
    }

    private List<MediaSourceRuntime> SnapshotRuntimes()
    {
        lock (_gate)
            return _runtimes.Values.ToList();
    }

    private async Task StopStartedAfterStartFailureAsync(
        List<MediaSourceRuntime> started,
        CancellationToken cancellationToken)
    {
        foreach (var runtime in started.AsEnumerable().Reverse())
        {
            try
            {
                await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.stop_failed",
                    $"Source '{runtime.Name}' failed to stop during start rollback.",
                    nameof(SourceRuntimeManager),
                    ex);
            }
        }
    }

    private static MediaSourceCapabilities ResolveCapabilities(MediaSourceTypeId typeId)
    {
        var value = typeId.Value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaSourceCapabilities
            {
                ProducesVideo = true,
                SupportsGpuFrames = true,
                HasStableFrameRate = true
            };
        }

        if (value.Contains("video.file", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaSourceCapabilities
            {
                ProducesVideo = true,
                SupportsGpuFrames = true,
                HasStableFrameRate = true,
                CanSeek = true
            };
        }

        return MediaSourceCapabilities.LiveGpuVideo;
    }

    private static MediaSourceBufferOptions ResolveBufferOptions(
        MediaSourceTypeId typeId,
        MediaSourceBufferOptions? explicitOptions)
    {
        if (explicitOptions is not null)
            return explicitOptions;

        var value = typeId.Value ?? string.Empty;

        if (value.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaSourceBufferOptions
            {
                Mode = MediaSourceBufferMode.Static,
                Capacity = 1
            };
        }

        if (value.Contains("video.file", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaSourceBufferOptions
            {
                Mode = MediaSourceBufferMode.TimelineDriven,
                Capacity = 2
            };
        }

        return new MediaSourceBufferOptions
        {
            Mode = MediaSourceBufferMode.KeepLatest,
            Capacity = 1
        };
    }
}
