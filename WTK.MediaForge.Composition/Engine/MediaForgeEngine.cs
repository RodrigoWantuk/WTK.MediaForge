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
    private EngineLifecycleState _lifecycleState = EngineLifecycleState.Idle;
    private long _bindingVersion;

    public MediaForgeEngine()
        : this(
            new UnsupportedMediaSourceProviderFactory(),
            new UnsupportedRenderOutputSinkFactory(),
            new UnsupportedRenderBackendFactory())
    {
    }

    public MediaForgeEngine(
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

    public bool IsRunning => _lifecycleState == EngineLifecycleState.Running;

    internal EngineLifecycleState LifecycleStateForTests => _lifecycleState;

    internal CompositionRuntime? RuntimeForTests => _runtime;

    internal MediaForgeRenderThread? RenderThreadForTests => _renderThread;

    internal IRenderBackend? BackendForTests => _backend;

    public async Task LoadProjectAsync(MediaForgeProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureNotRunning();

            var migrateResult = MediaForgeProjectMigrator.Migrate(project);
            if (!migrateResult.Success)
                migrateResult.Validation.ThrowIfInvalid();

            var validation = MediaForgeProjectValidator.Validate(migrateResult.Project!);
            validation.ThrowIfInvalid();

            CurrentProject = migrateResult.Project!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifecycleState is EngineLifecycleState.Running or EngineLifecycleState.Starting)
                return;

            _lifecycleState = EngineLifecycleState.Starting;

            try
            {
                MediaForgeProjectValidator.Validate(CurrentProject).ThrowIfInvalid();

                _runtime = new CompositionRuntime();
                _renderThreadGuard = new RenderThreadGuard();

                if (!_backendFactory.TryCreate(_renderThreadGuard, _diagnostics, out var backend) || backend is null)
                    throw new InvalidOperationException("Render backend could not be created.");

                _backend = backend;
                _renderThread = new MediaForgeRenderThread(_backend, _renderThreadGuard, diagnostics: _diagnostics);

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
                    EnqueueBindOutput(output, entry.Sink, entry.Target);
                }

                PublishCurrentRenderFrame();
                _lifecycleState = EngineLifecycleState.Running;
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
            if (_lifecycleState is EngineLifecycleState.Idle or EngineLifecycleState.Stopping)
                return;

            _lifecycleState = EngineLifecycleState.Stopping;

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
                        "engine.provider_stop_failed",
                        $"Failed to stop source provider '{provider.Name}'.",
                        nameof(MediaForgeEngine),
                        ex);
                }
            }

            _renderThread?.Dispose();
            _renderThread = null;
            _backend = null;
            _renderThreadGuard = null;

            foreach (var entry in _outputSinks.Values)
                await entry.Sink.DisposeAsync().ConfigureAwait(false);

            _outputSinks.Clear();
            _providers.Clear();
            _runtime = null;
            _projectState = null;
            _lifecycleState = EngineLifecycleState.Idle;
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();

            var editor = new MediaForgeProjectEditor(CurrentProject);
            edit(editor);
            editor.ValidateOrThrow();

            if (_lifecycleState == EngineLifecycleState.Running)
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

            if (_outputSinks.TryGetValue(outputId, out var existing))
                await existing.Sink.DisposeAsync().ConfigureAwait(false);

            var sink = _outputSinkFactory.CreateSink(target);
            _outputSinks[outputId] = new OutputSinkEntry(sink, target);

            if (_lifecycleState == EngineLifecycleState.Running)
                EnqueueBindOutput(output, sink, target);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnbindOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_outputSinks.Remove(outputId, out var entry))
                await entry.Sink.DisposeAsync().ConfigureAwait(false);

            if (_lifecycleState == EngineLifecycleState.Running)
                _renderThread?.EnqueueCommand(new UnbindOutputCommand { OutputId = outputId });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private void EnqueueBindOutput(
        MediaForgeRenderOutput output,
        IRenderOutputSink sink,
        RenderOutputTarget target)
    {
        var bindingVersion = Interlocked.Increment(ref _bindingVersion);
        var binding = sink.CreateBinding(output.Id, output.OutputSize, bindingVersion);
        _renderThread!.EnqueueCommand(new BindOutputCommand { Binding = binding });
    }

    private void PublishCurrentRenderFrame()
    {
        if (_runtime is null || _projectState is null || _renderThread is null)
            return;

        var buildResult = RenderFrameSnapshotFactory.Build(_projectState, _runtime, _diagnostics);
        using var snapshot = buildResult.TakeSnapshot();
        if (snapshot is not null)
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
            catch
            {
                // Best-effort rollback.
            }
        }

        _providers.Clear();
        _renderThread?.Dispose();
        _renderThread = null;
        _backend = null;
        _renderThreadGuard = null;
        _runtime = null;
        _projectState = null;
        _lifecycleState = EngineLifecycleState.Idle;
    }

    private void EnsureNotRunning()
    {
        if (_lifecycleState is EngineLifecycleState.Running or EngineLifecycleState.Starting or EngineLifecycleState.Stopping)
        {
            throw new InvalidOperationException("Operation is not allowed while the engine is running or transitioning.");
        }
    }

    private void EnsureCanMutateProject()
    {
        if (_lifecycleState is EngineLifecycleState.Starting or EngineLifecycleState.Stopping)
        {
            throw new InvalidOperationException("Project updates are not allowed while the engine is starting or stopping.");
        }
    }

    private sealed class OutputSinkEntry(IRenderOutputSink sink, RenderOutputTarget target)
    {
        public IRenderOutputSink Sink { get; } = sink;

        public RenderOutputTarget Target { get; } = target;
    }
}

internal sealed class UnsupportedRenderBackendFactory : IRenderBackendFactory
{
    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        backend = null;
        return false;
    }
}
