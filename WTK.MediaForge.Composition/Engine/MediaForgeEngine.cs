using System.Diagnostics;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using PublicRenderOutputSink = WTK.MediaForge.Composition.Outputs.IRenderOutputSink;
using RuntimeRenderOutputSink = WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngine : IAsyncDisposable
{
    private readonly IMediaSourceProviderFactory _sourceProviderFactory;
    private readonly IRenderOutputSinkFactory _outputSinkFactory;
    private readonly IRenderBackendFactory _backendFactory;
    private readonly IMediaForgeDiagnosticsSink? _externalDiagnostics;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<RenderOutputId, OutputSinkEntry> _outputSinks = [];
    private readonly RenderOutputSinkDispatcher _sinkDispatcher;

    private SourceRuntimeManager? _sourceRuntimeManager;
    private CompositionRuntime? _runtime;
    private RenderThreadGuard? _renderThreadGuard;
    private IRenderBackend? _backend;
    private MediaForgeRenderThread? _renderThread;
    private MediaForgeRenderPump? _renderPump;
    private long _renderFrameNumber;
    private TimeSpan _lastRenderPresentationTime;
    private ProjectStateSnapshot? _projectState;
    private MediaForgeProject? _currentProject;
    private MediaForgeEngineState _state = MediaForgeEngineState.Idle;
    private TimeSpan _sinkStopTimeout = TimeSpan.FromSeconds(5);
    private long _bindingVersion;
    private int _disposed;

    internal TimeSpan RenderThreadJoinTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan RenderThreadSubmissionShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan SinkStopTimeout
    {
        get => _sinkStopTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "SinkStopTimeout must be positive.");

            _sinkStopTimeout = value;
            _sinkDispatcher.SinkStopTimeout = value;
        }
    }

    internal double RenderFramesPerSecond { get; set; } = 60;

    internal TimeSpan RenderThreadJoinTimeoutForTests
    {
        get => RenderThreadJoinTimeout;
        set => RenderThreadJoinTimeout = value;
    }

    internal TimeSpan RenderThreadSubmissionShutdownTimeoutForTests
    {
        get => RenderThreadSubmissionShutdownTimeout;
        set => RenderThreadSubmissionShutdownTimeout = value;
    }

    internal MediaForgeEngine(
        IMediaSourceProviderFactory sourceProviderFactory,
        IRenderOutputSinkFactory outputSinkFactory,
        IRenderBackendFactory backendFactory,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _sourceProviderFactory = sourceProviderFactory ?? throw new ArgumentNullException(nameof(sourceProviderFactory));
        _outputSinkFactory = outputSinkFactory ?? throw new ArgumentNullException(nameof(outputSinkFactory));
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        _externalDiagnostics = diagnostics;
        _diagnostics = new EngineDiagnosticsSink(diagnostics, RaiseDiagnosticReported);
        _sinkDispatcher = new RenderOutputSinkDispatcher(_diagnostics, _sinkStopTimeout);
    }

    public bool HasProject => _currentProject is not null;

    public MediaForgeProject? CurrentProject =>
        _currentProject is null
            ? null
            : MediaForgeProjectCloner.DeepClone(_currentProject);

    public MediaForgeEngineState State => _state;

    public bool IsRunning => State == MediaForgeEngineState.Running;

    public event EventHandler<MediaForgeDiagnosticEventArgs>? DiagnosticReported;

    public event EventHandler<MediaForgeEngineStateChangedEventArgs>? StateChanged;

    public event EventHandler<MediaForgeFrameDroppedEventArgs>? FrameDropped;

    internal CompositionRuntime? RuntimeForTests => _runtime;

    internal MediaForgeRenderThread? RenderThreadForTests => _renderThread;

    internal MediaForgeRenderPump? RenderPumpForTests => _renderPump;

    internal IRenderBackend? BackendForTests => _backend;

    internal IRenderBackendFactory BackendFactoryForTests => _backendFactory;

    internal IMediaSourceProviderFactory SourceProviderFactoryForTests => _sourceProviderFactory;

    internal IRenderOutputSinkFactory OutputSinkFactoryForTests => _outputSinkFactory;

    internal IMediaForgeDiagnosticsSink? DiagnosticsForTests => _externalDiagnostics;

    internal ProjectStateSnapshot? ProjectStateForTests => _projectState;

    internal int OutputSinkCountForTests => _outputSinks.Count;

    internal int AttachedSinkCountForTests => _sinkDispatcher.SinkCount;

    internal RuntimeRenderOutputSink? GetOutputSinkForTests(RenderOutputId outputId) =>
        _outputSinks.TryGetValue(outputId, out var entry) ? entry.Sink : null;

    internal bool IsSinkAttachedForTests(RenderOutputId outputId, RenderOutputSinkId sinkId) =>
        _sinkDispatcher.IsSinkAttached(outputId, sinkId);

    public async Task LoadProjectAsync(MediaForgeProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureNotRunning();

            var ownedProject = MediaForgeProjectCloner.DeepClone(project);
            var migrateResult = MediaForgeProjectMigrator.Migrate(ownedProject);
            if (!migrateResult.Success)
                migrateResult.Validation.ThrowIfInvalid();

            var validation = MediaForgeProjectValidator.Validate(migrateResult.Project!);
            validation.ThrowIfInvalid();

            await ClearOutputBindingsAsync(cancellationToken).ConfigureAwait(false);
            await _sinkDispatcher.DetachAllAsync(cancellationToken).ConfigureAwait(false);

            _currentProject = migrateResult.Project!;
            SetState(MediaForgeEngineState.Loaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
                throw CreateEngineException("Engine cannot be started after entering a failed state.");

            if (State is MediaForgeEngineState.Running or MediaForgeEngineState.Starting)
                return;

            if (_currentProject is null)
                throw CreateEngineException("A project must be loaded before the engine can be started.");

            SetState(MediaForgeEngineState.Starting);

            try
            {
                var deadline = CreateDeadline(StartTimeout);
                MediaForgeProjectValidator.Validate(_currentProject).ThrowIfInvalid();

                _sourceRuntimeManager = new SourceRuntimeManager(_diagnostics);
                _runtime = new CompositionRuntime(_sourceRuntimeManager);
                _renderThreadGuard = new RenderThreadGuard();

                if (!_backendFactory.TryCreate(_renderThreadGuard, _diagnostics, out var backend) || backend is null)
                    throw CreateEngineException("Render backend could not be created.");

                _backend = backend;
                _renderThread = new MediaForgeRenderThread(
                    _backend,
                    _renderThreadGuard,
                    diagnostics: _diagnostics,
                    sinkDispatcher: _sinkDispatcher,
                    joinTimeout: RenderThreadJoinTimeout,
                    submissionShutdownTimeout: RenderThreadSubmissionShutdownTimeout);

                foreach (var sourceDefinition in _currentProject.SourceDefinitions)
                {
                    if (!_sourceProviderFactory.CanCreate(sourceDefinition.TypeId))
                    {
                        throw new MediaForgeUnsupportedFeatureException(
                            $"source.{sourceDefinition.TypeId.Value}",
                            $"No source provider factory registered for type '{sourceDefinition.TypeId.Value}'.");
                    }

                    var provider = _sourceProviderFactory.CreateProvider(sourceDefinition);
                    var sourceRuntime = _sourceRuntimeManager.RegisterProvider(provider, sourceDefinition);
                    await AwaitWithTimeoutAsync(
                        ct => sourceRuntime.StartAsync(ct),
                        GetRemainingTime(deadline),
                        $"Source provider '{sourceRuntime.Name}' did not start before StartTimeout.",
                        cancellationToken).ConfigureAwait(false);
                }

                _projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(_currentProject);
                _renderThread.Start();

                await EnsureSurfaceBindingsForAttachedSinksAsync(_currentProject, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (outputId, entry) in _outputSinks)
                {
                    var output = _currentProject.Outputs.First(o => o.Id == outputId);
                    await EnqueueBindOutputAsync(output, entry.Sink, entry.Target, cancellationToken)
                        .ConfigureAwait(false);
                }

                _renderPump = new MediaForgeRenderPump(
                    RenderFramesPerSecond,
                    CanPublishRenderFrame,
                    PublishCurrentRenderFrame,
                    _diagnostics);

                SetState(MediaForgeEngineState.Running);
                _renderPump.RequestFrame();
            }
            catch (Exception ex)
            {
                await RollbackStartAsync(cancellationToken).ConfigureAwait(false);

                if (IsTimeoutFailure(ex))
                    SetState(MediaForgeEngineState.Failed);

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
            {
                await CleanupRuntimeAsync(allowFailedState: true, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (State is MediaForgeEngineState.Idle or MediaForgeEngineState.Loaded or MediaForgeEngineState.Stopping or MediaForgeEngineState.Disposed)
                return;

            await CleanupRuntimeAsync(allowFailedState: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyProjectUpdateAsync(
        Action<MediaForgeProjectEditor> edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();

            var workingCopy = MediaForgeProjectCloner.DeepClone(_currentProject!);
            var editor = new MediaForgeProjectEditor(workingCopy);
            edit(editor);
            editor.ValidateOrThrow();

            _currentProject = workingCopy;

            if (State == MediaForgeEngineState.Running)
            {
                _projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(_currentProject);
                _renderPump?.RequestFrame();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BindOutputAsync(
        RenderOutputId outputId,
        RenderOutputTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
                throw CreateEngineException("Output binding is not allowed after the engine entered a failed state.");

            if (_currentProject is null)
                throw CreateEngineException("A project must be loaded before outputs can be bound.");

            var output = _currentProject.Outputs.FirstOrDefault(o => o.Id == outputId)
                ?? throw CreateEngineException($"Output {outputId} was not found in the current project.");

            if (output.TypeId != target.TypeId)
            {
                throw CreateEngineException(
                    $"Output type '{output.TypeId.Value}' does not match target type '{target.TypeId.Value}'.");
            }

            if (!_outputSinkFactory.CanCreate(target.TypeId))
            {
                throw new MediaForgeUnsupportedFeatureException(
                    $"output.{target.TypeId.Value}",
                    $"No output sink factory registered for type '{target.TypeId.Value}'.");
            }

            var newSink = _outputSinkFactory.CreateSink(target);
            OutputSinkEntry? oldEntry = null;
            var sinkAccepted = false;

            try
            {
                if (State == MediaForgeEngineState.Running)
                    await EnqueueBindOutputAsync(output, newSink, target, cancellationToken).ConfigureAwait(false);

                _outputSinks.TryGetValue(outputId, out oldEntry);
                _outputSinks[outputId] = new OutputSinkEntry(newSink, target, IsAutomaticForSinks: false);
                sinkAccepted = true;
            }
            finally
            {
                if (!sinkAccepted)
                    await newSink.DisposeAsync().ConfigureAwait(false);
            }

            if (oldEntry is not null)
                await oldEntry.Sink.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnbindOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
                throw CreateEngineException("Output unbinding is not allowed after the engine entered a failed state.");

            if (!_outputSinks.TryGetValue(outputId, out var entry))
                return;

            if (State == MediaForgeEngineState.Running && _renderThread is not null)
            {
                await AwaitCommandAsync(
                    _renderThread.EnqueueCommandAsync(new UnbindOutputCommand { OutputId = outputId }),
                    "Render output unbind command timed out.",
                    cancellationToken).ConfigureAwait(false);
            }

            _outputSinks.Remove(outputId);

            await entry.Sink.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AttachSinkAsync(
        RenderOutputId outputId,
        PublicRenderOutputSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
                throw CreateEngineException("Sink attachment is not allowed after the engine entered a failed state.");

            if (_currentProject is null)
                throw CreateEngineException("A project must be loaded before sinks can be attached.");

            var output = _currentProject.Outputs.FirstOrDefault(o => o.Id == outputId)
                ?? throw CreateEngineException($"Output {outputId} was not found in the current project.");

            if (output.TypeId != RenderOutputTypes.Offscreen)
            {
                throw new MediaForgeUnsupportedFeatureException(
                    $"output.{output.TypeId.Value}",
                    "Render output sinks currently require an offscreen render output surface.");
            }

            var createdSurfaceBinding = false;
            var attachSucceeded = false;

            try
            {
                createdSurfaceBinding = await EnsureAutomaticSurfaceBindingAsync(output, cancellationToken)
                    .ConfigureAwait(false);

                await _sinkDispatcher.AttachAsync(
                    output,
                    sink,
                    CommandTimeout,
                    cancellationToken).ConfigureAwait(false);

                attachSucceeded = true;
            }
            catch (Exception ex) when (IsTimeoutFailure(ex))
            {
                SetState(MediaForgeEngineState.Failed);
                if (ex is MediaForgeEngineException)
                    throw;

                throw new MediaForgeEngineException(
                    "Render output sink attach timed out.",
                    State,
                    ex);
            }
            finally
            {
                if (!attachSucceeded && createdSurfaceBinding)
                {
                    await RemoveSurfaceBindingAsync(outputId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DetachSinkAsync(
        RenderOutputId outputId,
        RenderOutputSinkId sinkId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MediaForgeEngineState.Failed)
                throw CreateEngineException("Sink detach is not allowed after the engine entered a failed state.");

            bool detached;
            try
            {
                detached = await AwaitWithTimeoutAsync(
                    ct => _sinkDispatcher.DetachAsync(outputId, sinkId, ct),
                    CommandTimeout,
                    "Render output sink detach timed out.",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTimeoutFailure(ex))
            {
                SetState(MediaForgeEngineState.Failed);
                if (ex is MediaForgeEngineException)
                    throw;

                throw new MediaForgeEngineException(
                    "Render output sink detach timed out.",
                    State,
                    ex);
            }

            if (!detached)
                return;

            if (!_sinkDispatcher.HasSinks(outputId) &&
                _outputSinks.TryGetValue(outputId, out var entry) &&
                entry.IsAutomaticForSinks)
            {
                await RemoveSurfaceBindingAsync(outputId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            List<Exception>? cleanupErrors = null;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await CleanupRuntimeAsync(allowFailedState: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (cleanupErrors ??= []).Add(ex);
                }

                try
                {
                    await _sinkDispatcher.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (cleanupErrors ??= []).Add(ex);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.sink_dispatcher_dispose_failed",
                        "Failed to dispose render output sink dispatcher during engine dispose.",
                        nameof(MediaForgeEngine),
                        ex);
                }

                if (cleanupErrors is null)
                    SetState(MediaForgeEngineState.Disposed);
            }
            finally
            {
                _gate.Release();
            }

            if (cleanupErrors is not null)
            {
                SetState(MediaForgeEngineState.Failed);
                throw new MediaForgeEngineException(
                    "Engine dispose cleanup failed after attempting all cleanup steps.",
                    State,
                    new AggregateException(cleanupErrors));
            }
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private Task EnqueueBindOutputAsync(
        MediaForgeRenderOutput output,
        RuntimeRenderOutputSink sink,
        RenderOutputTarget target,
        CancellationToken cancellationToken)
    {
        var bindingVersion = Interlocked.Increment(ref _bindingVersion);
        var binding = sink.CreateBinding(output.Id, output.OutputSize, bindingVersion);
        return AwaitCommandAsync(
            _renderThread!.EnqueueCommandAsync(new BindOutputCommand { Binding = binding }),
            "Render output bind command timed out.",
            cancellationToken);
    }

    private bool CanPublishRenderFrame()
    {
        var renderThread = _renderThread;
        return State == MediaForgeEngineState.Running &&
               renderThread is not null &&
               renderThread.CanAcceptPublishedFrame;
    }

    private void PublishCurrentRenderFrame()
    {
        if (_runtime is null || _projectState is null || _renderThread is null)
            return;

        var frameNumber = Interlocked.Increment(ref _renderFrameNumber);
        var delta = TimeSpan.FromSeconds(1d / RenderFramesPerSecond);
        var presentationTime = _lastRenderPresentationTime + delta;
        _lastRenderPresentationTime = presentationTime;

        var context = new RenderFrameContext(
            frameNumber,
            presentationTime,
            delta,
            RenderFramesPerSecond,
            CancellationToken.None);

        using var buildResult = RenderFrameSnapshotFactory.Build(_projectState, _runtime, context, _diagnostics);
        var snapshot = buildResult.TakeSnapshot();

        if (snapshot is null)
            return;

        _renderThread.PublishFrame(snapshot);
    }

    private async Task EnsureSurfaceBindingsForAttachedSinksAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken)
    {
        foreach (var output in project.Outputs)
        {
            if (_sinkDispatcher.HasSinks(output.Id))
                await EnsureAutomaticSurfaceBindingAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureAutomaticSurfaceBindingAsync(
        MediaForgeRenderOutput output,
        CancellationToken cancellationToken)
    {
        if (_outputSinks.ContainsKey(output.Id))
            return false;

        var target = new OffscreenRenderOutputTarget();
        if (!_outputSinkFactory.CanCreate(target.TypeId))
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"output.{target.TypeId.Value}",
                $"No output sink factory registered for type '{target.TypeId.Value}'.");
        }

        var sink = _outputSinkFactory.CreateSink(target);
        var accepted = false;

        try
        {
            if (State == MediaForgeEngineState.Running)
                await EnqueueBindOutputAsync(output, sink, target, cancellationToken).ConfigureAwait(false);

            _outputSinks[output.Id] = new OutputSinkEntry(sink, target, IsAutomaticForSinks: true);
            accepted = true;
            return true;
        }
        finally
        {
            if (!accepted)
                await sink.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveSurfaceBindingAsync(
        RenderOutputId outputId,
        CancellationToken cancellationToken)
    {
        if (!_outputSinks.TryGetValue(outputId, out var entry))
            return;

        if (State == MediaForgeEngineState.Running && _renderThread is not null)
        {
            await AwaitCommandAsync(
                _renderThread.EnqueueCommandAsync(new UnbindOutputCommand { OutputId = outputId }),
                "Render output unbind command timed out.",
                cancellationToken).ConfigureAwait(false);
        }

        _outputSinks.Remove(outputId);
        await entry.Sink.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ClearOutputBindingsAsync(CancellationToken cancellationToken)
    {
        foreach (var outputId in _outputSinks.Keys.ToArray())
            await RemoveSurfaceBindingAsync(outputId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RollbackStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CleanupRuntimeAsync(allowFailedState: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "engine.start_rollback_cleanup_failed",
                "Failed to cleanup runtime during start rollback.",
                nameof(MediaForgeEngine),
                ex);
        }
    }

    private async Task CleanupRuntimeAsync(
        bool allowFailedState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var originalState = State;
        if (originalState == MediaForgeEngineState.Disposed)
            return;

        if (originalState == MediaForgeEngineState.Failed && !allowFailedState)
            return;

        if (originalState is MediaForgeEngineState.Idle or MediaForgeEngineState.Loaded &&
            _renderPump is null &&
            _renderThread is null &&
            _backend is null &&
            (_sourceRuntimeManager?.Count ?? 0) == 0 &&
            _runtime is null &&
            _projectState is null)
        {
            return;
        }

        if (originalState != MediaForgeEngineState.Failed)
            SetState(MediaForgeEngineState.Stopping);

        var cleanupErrors = new List<Exception>();

        var renderPump = _renderPump;
        _renderPump = null;

        if (renderPump is not null)
        {
            try
            {
                await renderPump.StopAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.render_pump_stop_failed",
                    "Failed to stop render pump during engine cleanup.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        var sourceRuntimeManager = _sourceRuntimeManager;
        if (sourceRuntimeManager is not null)
        {
            try
            {
                await sourceRuntimeManager.StopAllAsync(
                    async (sourceRuntime, ct) =>
                    {
                        await AwaitWithTimeoutAsync(
                            innerCt => sourceRuntime.StopAsync(innerCt),
                            StopTimeout,
                            $"Source provider '{sourceRuntime.Name}' did not stop before StopTimeout.",
                            ct).ConfigureAwait(false);
                    },
                    (sourceRuntime, ex) =>
                    {
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "engine.provider_stop_failed",
                            $"Failed to stop source provider '{sourceRuntime.Name}'.",
                            nameof(MediaForgeEngine),
                            ex);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aggregate)
                    cleanupErrors.AddRange(aggregate.InnerExceptions);
                else
                    cleanupErrors.Add(ex);
            }
        }

        var renderThread = _renderThread;
        var backend = _backend;
        var renderThreadStopped = true;

        if (renderThread is not null)
        {
            try
            {
                renderThread.Dispose();
                renderThreadStopped = !renderThread.IsRunning;
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
                renderThreadStopped = !renderThread.IsRunning;
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.render_thread_dispose_failed",
                    "Failed to dispose render thread during engine cleanup.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        if (renderThreadStopped)
        {
            _renderThread = null;
            _renderThreadGuard = null;
        }

        if (backend is not null)
        {
            if (renderThreadStopped)
            {
                try
                {
                    backend.Dispose();
                    _backend = null;
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(ex);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.render_backend_dispose_failed",
                        "Failed to dispose render backend during engine cleanup.",
                        nameof(MediaForgeEngine),
                        ex);
                }
            }
            else
            {
                var ex = new InvalidOperationException(
                    "Render backend was not disposed because the render thread is still alive.");
                cleanupErrors.Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Fatal,
                    "engine.backend_dispose_skipped_render_thread_alive",
                    ex.Message,
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        if (originalState != MediaForgeEngineState.Starting)
        {
            foreach (var entry in _outputSinks.Values)
            {
                try
                {
                    await entry.Sink.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(ex);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.output_sink_dispose_failed",
                        "Failed to dispose output sink during engine cleanup.",
                        nameof(MediaForgeEngine),
                        ex);
                }
            }

            _outputSinks.Clear();
        }

        _sourceRuntimeManager?.Clear();
        _sourceRuntimeManager = null;
        _runtime = null;
        _projectState = null;

        SetState(renderThreadStopped && cleanupErrors.Count == 0
            ? (_currentProject is null ? MediaForgeEngineState.Idle : MediaForgeEngineState.Loaded)
            : MediaForgeEngineState.Failed);

        if (cleanupErrors.Count > 0)
        {
            throw new MediaForgeEngineException(
                "Engine runtime cleanup failed after attempting all cleanup steps.",
                State,
                new AggregateException(cleanupErrors));
        }
    }

    private void EnsureNotRunning()
    {
        if (State is MediaForgeEngineState.Running or MediaForgeEngineState.Starting or MediaForgeEngineState.Stopping)
        {
            throw CreateEngineException("Operation is not allowed while the engine is running or transitioning.");
        }

        if (State == MediaForgeEngineState.Failed)
            throw CreateEngineException("Operation is not allowed after the engine entered a failed state.");
    }

    private void EnsureCanMutateProject()
    {
        if (_currentProject is null)
            throw CreateEngineException("A project must be loaded before it can be updated.");

        if (State is MediaForgeEngineState.Starting or MediaForgeEngineState.Stopping)
        {
            throw CreateEngineException("Project updates are not allowed while the engine is starting or stopping.");
        }

        if (State == MediaForgeEngineState.Failed)
            throw CreateEngineException("Project updates are not allowed after the engine entered a failed state.");
    }

    private void SetState(MediaForgeEngineState newState)
    {
        var oldState = State;
        if (oldState == newState)
            return;

        _state = newState;
        RaiseStateChanged(oldState, newState);
    }

    private MediaForgeEngineException CreateEngineException(string message) =>
        new(message, State);

    private async Task AwaitCommandAsync(
        Task commandTask,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await AwaitStartedTaskWithTimeoutAsync(
                commandTask,
                CommandTimeout,
                timeoutMessage,
                cancellationToken).ConfigureAwait(false);
        }
        catch (MediaForgeEngineException ex) when (IsTimeoutFailure(ex))
        {
            SetState(MediaForgeEngineState.Failed);
            throw;
        }
    }

    private async Task<T> AwaitStartedTaskWithTimeoutAsync<T>(
        Task<T> operation,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new MediaForgeEngineException(timeoutMessage, State, ex);
        }
    }

    private async Task AwaitStartedTaskWithTimeoutAsync(
        Task operation,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new MediaForgeEngineException(timeoutMessage, State, ex);
        }
    }

    private async Task<T> AwaitWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var task = operation(linked.Token);

        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            timeoutCts.Cancel();
            throw new MediaForgeEngineException(timeoutMessage, State, ex);
        }
        catch (OperationCanceledException ex) when (
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            timeoutCts.Cancel();
            throw new MediaForgeEngineException(timeoutMessage, State, new TimeoutException(timeoutMessage, ex));
        }
    }

    private async Task AwaitWithTimeoutAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var task = operation(linked.Token);

        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            timeoutCts.Cancel();
            throw new MediaForgeEngineException(timeoutMessage, State, ex);
        }
        catch (OperationCanceledException ex) when (
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            timeoutCts.Cancel();
            throw new MediaForgeEngineException(timeoutMessage, State, new TimeoutException(timeoutMessage, ex));
        }
    }

    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts;
        }

        return new CancellationTokenSource(timeout);
    }

    private static long CreateDeadline(TimeSpan timeout) =>
        Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan GetRemainingTime(long deadline)
    {
        var remainingTicks = deadline - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private static bool IsTimeoutFailure(Exception exception) =>
        exception is TimeoutException ||
        exception is MediaForgeEngineException { InnerException: TimeoutException } ||
        exception is AggregateException aggregate &&
        aggregate.InnerExceptions.Any(IsTimeoutFailure);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private void RaiseDiagnosticReported(MediaForgeDiagnostic diagnostic)
    {
        SafeRaiseEvent(
            nameof(DiagnosticReported),
            () => DiagnosticReported?.Invoke(this, new MediaForgeDiagnosticEventArgs(diagnostic)));

        if (diagnostic.Code is "render.frame_dropped_tracker_full" or
            "engine.render_pump_frame_dropped_backpressure" or
            "sink.frame_dropped_backpressure")
        {
            SafeRaiseEvent(
                nameof(FrameDropped),
                () => FrameDropped?.Invoke(this, new MediaForgeFrameDroppedEventArgs(diagnostic)));
        }
    }

    private void RaiseStateChanged(MediaForgeEngineState oldState, MediaForgeEngineState newState) =>
        SafeRaiseEvent(
            nameof(StateChanged),
            () => StateChanged?.Invoke(this, new MediaForgeEngineStateChangedEventArgs(oldState, newState)));

    private void SafeRaiseEvent(string eventName, Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _externalDiagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "engine.event_handler_failed",
                $"Engine event handler '{eventName}' failed.",
                nameof(MediaForgeEngine),
                ex);
        }
    }

    private sealed class EngineDiagnosticsSink(
        IMediaForgeDiagnosticsSink? inner,
        Action<MediaForgeDiagnostic> onDiagnostic)
        : IMediaForgeDiagnosticsSink
    {
        public void Report(MediaForgeDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            inner?.Report(diagnostic);
            onDiagnostic(diagnostic);
        }
    }

    private sealed class OutputSinkEntry(
        RuntimeRenderOutputSink sink,
        RenderOutputTarget target,
        bool IsAutomaticForSinks)
    {
        public RuntimeRenderOutputSink Sink { get; } = sink;

        public RenderOutputTarget Target { get; } = target;

        public bool IsAutomaticForSinks { get; } = IsAutomaticForSinks;
    }
}
