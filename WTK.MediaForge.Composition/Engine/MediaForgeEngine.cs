using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngine : IAsyncDisposable
{
    private readonly IMediaSourceProviderFactory _sourceProviderFactory;
    private readonly IRenderOutputSinkFactory _outputSinkFactory;
    private readonly IRenderBackendFactory _backendFactory;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<IVideoFrameProvider> _providers = [];
    private readonly Dictionary<RenderOutputId, OutputSinkEntry> _outputSinks = [];

    private CompositionRuntime? _runtime;
    private RenderThreadGuard? _renderThreadGuard;
    private IRenderBackend? _backend;
    private MediaForgeRenderThread? _renderThread;
    private ProjectStateSnapshot? _projectState;
    private MediaForgeEngineState _state = MediaForgeEngineState.Idle;
    private long _bindingVersion;
    private int _disposed;

    internal TimeSpan RenderThreadJoinTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan RenderThreadSubmissionShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

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
        _diagnostics = diagnostics;
    }

    public MediaForgeProject CurrentProject { get; private set; } = new();

    public MediaForgeEngineState State => _state;

    public bool IsRunning => State == MediaForgeEngineState.Running;

    internal CompositionRuntime? RuntimeForTests => _runtime;

    internal MediaForgeRenderThread? RenderThreadForTests => _renderThread;

    internal IRenderBackend? BackendForTests => _backend;

    internal IRenderBackendFactory BackendFactoryForTests => _backendFactory;

    internal IMediaSourceProviderFactory SourceProviderFactoryForTests => _sourceProviderFactory;

    internal IRenderOutputSinkFactory OutputSinkFactoryForTests => _outputSinkFactory;

    internal IMediaForgeDiagnosticsSink? DiagnosticsForTests => _diagnostics;

    internal ProjectStateSnapshot? ProjectStateForTests => _projectState;

    internal int OutputSinkCountForTests => _outputSinks.Count;

    internal IRenderOutputSink? GetOutputSinkForTests(RenderOutputId outputId) =>
        _outputSinks.TryGetValue(outputId, out var entry) ? entry.Sink : null;

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

            CurrentProject = migrateResult.Project!;
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
                throw new InvalidOperationException("Engine cannot be started after entering a failed state.");

            if (State is MediaForgeEngineState.Running or MediaForgeEngineState.Starting)
                return;

            SetState(MediaForgeEngineState.Starting);

            try
            {
                MediaForgeProjectValidator.Validate(CurrentProject).ThrowIfInvalid();

                _runtime = new CompositionRuntime();
                _renderThreadGuard = new RenderThreadGuard();

                if (!_backendFactory.TryCreate(_renderThreadGuard, _diagnostics, out var backend) || backend is null)
                    throw new InvalidOperationException("Render backend could not be created.");

                _backend = backend;
                _renderThread = new MediaForgeRenderThread(
                    _backend,
                    _renderThreadGuard,
                    diagnostics: _diagnostics,
                    joinTimeout: RenderThreadJoinTimeout,
                    submissionShutdownTimeout: RenderThreadSubmissionShutdownTimeout);

                var startedProviders = new List<IVideoFrameProvider>();
                foreach (var sourceDefinition in CurrentProject.SourceDefinitions)
                {
                    if (!_sourceProviderFactory.CanCreate(sourceDefinition.TypeId))
                    {
                        throw new NotSupportedException(
                            $"No source provider factory registered for type '{sourceDefinition.TypeId.Value}'.");
                    }

                    var provider = _sourceProviderFactory.CreateProvider(sourceDefinition);
                    _runtime.RegisterFrameProvider(provider);
                    _providers.Add(provider);
                    await provider.StartAsync(cancellationToken).ConfigureAwait(false);
                    startedProviders.Add(provider);
                }

                _projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(CurrentProject);
                _renderThread.Start();

                foreach (var (outputId, entry) in _outputSinks)
                {
                    var output = CurrentProject.Outputs.First(o => o.Id == outputId);
                    await EnqueueBindOutputAsync(output, entry.Sink, entry.Target).ConfigureAwait(false);
                }

                PublishCurrentRenderFrame();
                SetState(MediaForgeEngineState.Running);
            }
            catch
            {
                await RollbackStartAsync(cancellationToken).ConfigureAwait(false);
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
            if (State is MediaForgeEngineState.Idle or MediaForgeEngineState.Stopping or MediaForgeEngineState.Failed or MediaForgeEngineState.Disposed)
                return;

            SetState(MediaForgeEngineState.Stopping);

            var cleanupErrors = new List<Exception>();

            foreach (var provider in _providers.AsEnumerable().Reverse())
            {
                try
                {
                    await provider.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(ex);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.provider_stop_failed",
                        $"Failed to stop source provider '{provider.Name}'.",
                        nameof(MediaForgeEngine),
                        ex);
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
                        "Failed to dispose render thread during engine stop.",
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
                            "Failed to dispose render backend during engine stop.",
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
                        "Failed to dispose output sink during engine stop.",
                        nameof(MediaForgeEngine),
                        ex);
                }
            }

            _outputSinks.Clear();
            _providers.Clear();
            _runtime = null;
            _projectState = null;
            SetState(renderThreadStopped && cleanupErrors.Count == 0
                ? MediaForgeEngineState.Idle
                : MediaForgeEngineState.Failed);

            if (cleanupErrors.Count > 0)
            {
                throw new AggregateException(
                    "Engine stop cleanup failed after attempting all cleanup steps.",
                    cleanupErrors);
            }
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

            var workingCopy = MediaForgeProjectCloner.DeepClone(CurrentProject);
            var editor = new MediaForgeProjectEditor(workingCopy);
            edit(editor);
            editor.ValidateOrThrow();

            CurrentProject = workingCopy;

            if (State == MediaForgeEngineState.Running)
            {
                _projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(CurrentProject);
                PublishCurrentRenderFrame();
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
                throw new InvalidOperationException("Output binding is not allowed after the engine entered a failed state.");

            var output = CurrentProject.Outputs.FirstOrDefault(o => o.Id == outputId)
                ?? throw new InvalidOperationException($"Output {outputId} was not found in the current project.");

            if (output.TypeId != target.TypeId)
            {
                throw new InvalidOperationException(
                    $"Output type '{output.TypeId.Value}' does not match target type '{target.TypeId.Value}'.");
            }

            if (!_outputSinkFactory.CanCreate(target.TypeId))
            {
                throw new NotSupportedException(
                    $"No output sink factory registered for type '{target.TypeId.Value}'.");
            }

            var newSink = _outputSinkFactory.CreateSink(target);
            OutputSinkEntry? oldEntry = null;
            var sinkAccepted = false;

            try
            {
                if (State == MediaForgeEngineState.Running)
                    await EnqueueBindOutputAsync(output, newSink, target).ConfigureAwait(false);

                _outputSinks.TryGetValue(outputId, out oldEntry);
                _outputSinks[outputId] = new OutputSinkEntry(newSink, target);
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
                throw new InvalidOperationException("Output unbinding is not allowed after the engine entered a failed state.");

            if (!_outputSinks.TryGetValue(outputId, out var entry))
                return;

            if (State == MediaForgeEngineState.Running && _renderThread is not null)
                await _renderThread.EnqueueCommandAsync(new UnbindOutputCommand { OutputId = outputId }).ConfigureAwait(false);

            _outputSinks.Remove(outputId);

            await entry.Sink.DisposeAsync().ConfigureAwait(false);
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
            if (State != MediaForgeEngineState.Failed)
                await StopAsync().ConfigureAwait(false);

            if (State != MediaForgeEngineState.Failed)
                SetState(MediaForgeEngineState.Disposed);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private Task EnqueueBindOutputAsync(
        MediaForgeRenderOutput output,
        IRenderOutputSink sink,
        RenderOutputTarget target)
    {
        var bindingVersion = Interlocked.Increment(ref _bindingVersion);
        var binding = sink.CreateBinding(output.Id, output.OutputSize, bindingVersion);
        return _renderThread!.EnqueueCommandAsync(new BindOutputCommand { Binding = binding });
    }

    private void PublishCurrentRenderFrame()
    {
        if (_runtime is null || _projectState is null || _renderThread is null)
            return;

        using var buildResult = RenderFrameSnapshotFactory.Build(_projectState, _runtime, _diagnostics);
        var snapshot = buildResult.TakeSnapshot();

        if (snapshot is null)
            return;

        _renderThread.PublishFrame(snapshot);
    }

    private async Task RollbackStartAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _providers.AsEnumerable().Reverse())
        {
            try
            {
                await provider.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.start_rollback_provider_stop_failed",
                    "Failed to stop source provider during start rollback.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        _providers.Clear();

        var renderThread = _renderThread;
        var backend = _backend;
        _renderThread = null;
        _backend = null;
        _renderThreadGuard = null;

        if (renderThread is not null)
        {
            try
            {
                renderThread.Dispose();
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.start_rollback_render_thread_dispose_failed",
                    "Failed to dispose render thread during start rollback.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        if (backend is not null)
        {
            try
            {
                backend.Dispose();
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.start_rollback_render_backend_dispose_failed",
                    "Failed to dispose render backend during start rollback.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        _runtime = null;
        _projectState = null;
        SetState(MediaForgeEngineState.Idle);
    }

    private void EnsureNotRunning()
    {
        if (State is MediaForgeEngineState.Running or MediaForgeEngineState.Starting or MediaForgeEngineState.Stopping)
        {
            throw new InvalidOperationException("Operation is not allowed while the engine is running or transitioning.");
        }

        if (State == MediaForgeEngineState.Failed)
            throw new InvalidOperationException("Operation is not allowed after the engine entered a failed state.");
    }

    private void EnsureCanMutateProject()
    {
        if (State is MediaForgeEngineState.Starting or MediaForgeEngineState.Stopping)
        {
            throw new InvalidOperationException("Project updates are not allowed while the engine is starting or stopping.");
        }

        if (State == MediaForgeEngineState.Failed)
            throw new InvalidOperationException("Project updates are not allowed after the engine entered a failed state.");
    }

    private void SetState(MediaForgeEngineState newState) => _state = newState;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class OutputSinkEntry(IRenderOutputSink sink, RenderOutputTarget target)
    {
        public IRenderOutputSink Sink { get; } = sink;

        public RenderOutputTarget Target { get; } = target;
    }
}

