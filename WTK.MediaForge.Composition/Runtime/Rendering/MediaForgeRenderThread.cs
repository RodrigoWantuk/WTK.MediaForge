using System.Collections.Concurrent;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class MediaForgeRenderThread : IDisposable
{
    private readonly IRenderBackend _backend;
    private readonly RenderThreadGuard _threadGuard;
    private readonly PendingRenderSubmissionTracker _pendingTracker;
    private readonly LatestSnapshotBuffer _snapshotBuffer = new();
    private readonly ConcurrentQueue<RenderCommand> _commands = new();
    private readonly ManualResetEventSlim _workSignal = new(false);
    private readonly Thread _thread;
    private int _disposed;
    private volatile int _stopRequested;
    private Exception? _shutdownCleanupError;

    public MediaForgeRenderThread(
        IRenderBackend backend,
        RenderThreadGuard threadGuard,
        PendingRenderSubmissionTracker? pendingTracker = null,
        int maxFramesInFlight = 2)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _pendingTracker = pendingTracker ?? new PendingRenderSubmissionTracker(maxFramesInFlight);
        _thread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "MediaForge.Render"
        };
    }

    internal PendingRenderSubmissionTracker PendingTracker => _pendingTracker;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(command);

        _commands.Enqueue(command);

        if (command is StopRenderThreadCommand)
            _stopRequested = 1;

        _workSignal.Set();
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
                catch (Exception)
                {
                    // TODO: Diagnostics.Record snapshot dispose failure.
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

        try
        {
            if (_thread.IsAlive)
            {
                _stopRequested = 1;
                _commands.Enqueue(new StopRenderThreadCommand());
                _workSignal.Set();

                if (!_thread.Join(TimeSpan.FromSeconds(10)))
                    stopException = new TimeoutException("Render thread did not stop within the expected timeout.");
            }
        }
        finally
        {
            _workSignal.Dispose();
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
                RenderLatestSnapshot();
                _workSignal.Wait(50);
                _workSignal.Reset();
            }

            ProcessCommands(maxCommands: null);
            _pendingTracker.PollCompleted();
            _backend.WaitIdle();
            _pendingTracker.Dispose();
            _snapshotBuffer.Dispose();
        }
        catch (Exception ex)
        {
            _shutdownCleanupError = ex;
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

            processed++;
            if (maxCommands is int limit && processed >= limit)
                break;
        }
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
                return;

            _threadGuard.AssertOnRenderThread();
            submission = _backend.Submit(snapshot);
            _pendingTracker.Add(submission);
            ownershipTransferred = true;
        }
        catch (Exception)
        {
            // TODO: Diagnostics.Record render failure.
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (submission is not null)
                {
                    try
                    {
                        submission.Dispose();
                    }
                    catch (Exception)
                    {
                        // TODO: Diagnostics.Record submission dispose failure.
                    }
                }
                else
                {
                    try
                    {
                        snapshot.Dispose();
                    }
                    catch (Exception)
                    {
                        // TODO: Diagnostics.Record snapshot dispose failure.
                    }
                }
            }
        }
    }
}
