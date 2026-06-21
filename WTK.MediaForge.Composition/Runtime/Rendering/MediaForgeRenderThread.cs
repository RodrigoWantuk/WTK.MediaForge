using System.Collections.Concurrent;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class MediaForgeRenderThread : IDisposable
{
    private static readonly TimeSpan FailedSubmitDisposeTimeout = TimeSpan.FromSeconds(1);

    private readonly IRenderBackend _backend;
    private readonly RenderThreadGuard _threadGuard;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly PendingRenderSubmissionTracker _pendingTracker;
    private readonly LatestSnapshotBuffer _snapshotBuffer = new();
    private readonly ConcurrentQueue<RenderCommand> _commands = new();
    private readonly AutoResetEvent _workSignal = new(false);
    private readonly Thread _thread;
    private readonly TimeSpan _joinTimeout;
    private readonly TimeSpan _submissionShutdownTimeout;
    private int _disposed;
    private int _workSignalDisposed;
    private volatile int _stopRequested;
    private Exception? _shutdownCleanupError;

    public MediaForgeRenderThread(
        IRenderBackend backend,
        RenderThreadGuard threadGuard,
        PendingRenderSubmissionTracker? pendingTracker = null,
        int maxFramesInFlight = 2,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        RenderOutputSinkDispatcher? sinkDispatcher = null,
        TimeSpan? joinTimeout = null,
        TimeSpan? submissionShutdownTimeout = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _diagnostics = diagnostics;
        _pendingTracker = pendingTracker ?? new PendingRenderSubmissionTracker(
            maxFramesInFlight,
            diagnostics,
            sinkDispatcher);
        _joinTimeout = joinTimeout ?? TimeSpan.FromSeconds(10);
        _submissionShutdownTimeout = submissionShutdownTimeout ?? TimeSpan.FromSeconds(10);
        _thread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "MediaForge.Render"
        };
    }

    internal PendingRenderSubmissionTracker PendingTracker => _pendingTracker;

    internal bool CanAcceptPublishedFrame =>
        _pendingTracker.CanAcceptFrame && !_snapshotBuffer.HasPending;

    internal bool WorkSignalDisposedForTests => Volatile.Read(ref _workSignalDisposed) != 0;

    public bool IsRunning => _thread.IsAlive;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_thread.IsAlive)
            return;

        _thread.Start();
    }

    public void EnqueueCommand(RenderCommand command)
    {
        _ = EnqueueCommandAsync(command);
    }

    public Task EnqueueCommandAsync(RenderCommand command)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(command);

        _commands.Enqueue(command);

        if (command is StopRenderThreadCommand)
            _stopRequested = 1;

        _workSignal.Set();

        return command.Completion;
    }

    public void PublishFrame(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var acceptedByBuffer = false;

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _snapshotBuffer.Publish(snapshot);
            acceptedByBuffer = true;
            _workSignal.Set();
        }
        catch
        {
            if (!acceptedByBuffer)
            {
                try
                {
                    snapshot.Dispose();
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "render.snapshot_dispose_failed",
                        "Failed to dispose unpublished render snapshot.",
                        nameof(MediaForgeRenderThread),
                        ex);
                }
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Exception? stopException = null;
        var threadStopped = true;

        try
        {
            if (_thread.IsAlive)
            {
                _stopRequested = 1;
                _commands.Enqueue(new StopRenderThreadCommand());
                _workSignal.Set();

                threadStopped = _thread.Join(_joinTimeout);
                if (!threadStopped)
                {
                    stopException = new TimeoutException("Render thread did not stop within the expected timeout.");

                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "render.thread_stop_timeout",
                        stopException.Message,
                        nameof(MediaForgeRenderThread),
                        stopException);
                }
            }
        }
        finally
        {
            if (threadStopped || !_thread.IsAlive)
            {
                _workSignal.Dispose();
                Volatile.Write(ref _workSignalDisposed, 1);
            }
        }

        if (_shutdownCleanupError is not null)
            throw new InvalidOperationException("Render thread shutdown cleanup failed.", _shutdownCleanupError);

        if (stopException is not null)
            throw stopException;
    }

    private void RenderLoop()
    {
        _threadGuard.BindToCurrentThread();

        try
        {
            while (_stopRequested == 0)
            {
                ProcessCommands(maxCommands: null);

                if (_stopRequested == 0)
                    RenderLatestSnapshot();

                if (_stopRequested != 0)
                    break;

                if (!_commands.IsEmpty || _snapshotBuffer.HasPending)
                    continue;

                _pendingTracker.PollCompleted();

                if (!_commands.IsEmpty || _snapshotBuffer.HasPending)
                    continue;

                var idleWait = _pendingTracker.PendingCount > 0
                    ? TimeSpan.FromMilliseconds(16)
                    : Timeout.InfiniteTimeSpan;

                _workSignal.WaitOne(idleWait);
            }

            ProcessCommands(maxCommands: null);
            _snapshotBuffer.Dispose();
            _pendingTracker
                .ShutdownAsync(_backend, _submissionShutdownTimeout, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _shutdownCleanupError = ex;
            FailPendingCommands(ex);
        }
        finally
        {
            _threadGuard.Clear();
        }
    }

    private void ProcessCommands(int? maxCommands)
    {
        var processed = 0;

        while (_commands.TryDequeue(out var command))
        {
            try
            {
                switch (command)
                {
                    case BindOutputCommand bind:
                        _threadGuard.AssertOnRenderThread();
                        _backend.BindOutput(bind.Binding);
                        break;
                    case UnbindOutputCommand unbind:
                        _threadGuard.AssertOnRenderThread();
                        _backend.UnbindOutput(unbind.OutputId);
                        break;
                    case ResizeOutputCommand resize:
                        _threadGuard.AssertOnRenderThread();
                        _backend.ResizeOutput(resize.OutputId, resize.SurfaceSize);
                        break;
                    case StopRenderThreadCommand:
                        _stopRequested = 1;
                        break;
                }

                command.Complete();
            }
            catch (Exception ex)
            {
                command.Fail(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "render.command_failed",
                    "Render command failed.",
                    nameof(MediaForgeRenderThread),
                    ex);
            }

            processed++;
            if (maxCommands is int limit && processed >= limit)
                break;
        }
    }

    private void FailPendingCommands(Exception exception)
    {
        while (_commands.TryDequeue(out var command))
            command.Fail(exception);
    }

    private void RenderLatestSnapshot()
    {
        _threadGuard.AssertOnRenderThread();
        _pendingTracker.PollCompleted();

        var snapshot = _snapshotBuffer.AcquireLatest();
        if (snapshot is null)
            return;

        IRenderFrameSubmission? submission = null;
        var ownershipTransferred = false;

        try
        {
            if (!_pendingTracker.CanAcceptFrame)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Warning,
                    "render.frame_dropped_tracker_full",
                    "Render frame dropped because the pending submission tracker is full.",
                    nameof(MediaForgeRenderThread));
                return;
            }

            _threadGuard.AssertOnRenderThread();
            submission = _backend.Submit(snapshot);
            _pendingTracker.Add(submission);
            ownershipTransferred = true;
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "render.submit_failed",
                "Render backend submit failed.",
                nameof(MediaForgeRenderThread),
                ex);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (submission is not null)
                {
                    try
                    {
                        submission
                            .WaitForCompletionAsync(FailedSubmitDisposeTimeout, CancellationToken.None)
                            .AsTask()
                            .GetAwaiter()
                            .GetResult();
                        submission.DisposeCompleted();
                    }
                    catch (Exception ex)
                    {
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "render.submission_dispose_failed",
                            "Failed to dispose render submission after submit failure.",
                            nameof(MediaForgeRenderThread),
                            ex);
                    }
                }
                else
                {
                    try
                    {
                        snapshot.Dispose();
                    }
                    catch (Exception ex)
                    {
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "render.snapshot_dispose_failed",
                            "Failed to dispose render snapshot after submit failure.",
                            nameof(MediaForgeRenderThread),
                            ex);
                    }
                }
            }
        }
    }
}
