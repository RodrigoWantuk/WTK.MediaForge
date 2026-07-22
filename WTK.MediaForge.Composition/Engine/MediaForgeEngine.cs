using System.Diagnostics;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Recovery;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Scenes.Graph;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using PublicRenderOutputSink = WTK.MediaForge.Composition.Outputs.IRenderOutputSink;
using RuntimeRenderOutputSink = WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeEngine : IAsyncDisposable
{
    private readonly IMediaSourceProviderFactory _sourceProviderFactory;
    private readonly IRenderOutputSinkFactory _outputSinkFactory;
    private readonly IEncodedOutputRouteFactory? _encodedOutputRouteFactory;
    private readonly IRenderBackendFactory _backendFactory;
    private readonly IMediaForgeDiagnosticsSink? _externalDiagnostics;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly EngineLifecycleCoordinator _lifecycleCoordinator = new();
    private readonly EngineProjectCoordinator _projectCoordinator = new();
    private readonly EngineOutputRouteCoordinator<OutputSinkEntry> _outputRouteCoordinator = new();
    private readonly SceneEditSessionCoordinator<ActiveSceneEditSession> _sceneEditSessionCoordinator = new();
    private readonly EngineRecoveryCoordinator _engineRecoveryCoordinator = new();
    private SemaphoreSlim _gate => _lifecycleCoordinator.Gate;
    private Dictionary<RenderOutputId, OutputSinkEntry> _outputSinks => _outputRouteCoordinator.Sinks;
    private Dictionary<SceneEditSessionId, ActiveSceneEditSession> _sceneEditSessions => _sceneEditSessionCoordinator.Sessions;
    private readonly RenderOutputSinkDispatcher _sinkDispatcher;
    private readonly OutputRouteTransitionRuntime _outputRouteTransitions = new();
    private readonly object _sceneRouteTransitionGate = new();
    private readonly Dictionary<Guid, ActiveOutputSceneTransition> _sceneRouteTransitions = [];

    private SourceRuntimeManager? _sourceRuntimeManager;
    private CompositionRuntime? _runtime;
    private MediaPipelineRuntime? _mediaPipelineRuntime;
    private SceneRuntime? _sceneRuntime;
    private FaultRecoveryCoordinator? _faultRecoveryCoordinator;
    private RenderThreadGuard? _renderThreadGuard;
    private IRenderBackend? _backend;
    private MediaForgeRenderThread? _renderThread;
    private MediaForgeRenderPump? _renderPump;
    private ProjectStateSnapshot? _projectState;
    private MediaForgeProject? _currentProject
    {
        get => _projectCoordinator.Current;
        set => _projectCoordinator.Current = value;
    }
    private TimeSpan _sinkStopTimeout = TimeSpan.FromSeconds(5);
    private long _bindingVersion;
    private CancellationTokenSource? _recoveryCancellation;

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
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IEncodedOutputRouteFactory? encodedOutputRouteFactory = null)
    {
        _sourceProviderFactory = sourceProviderFactory ?? throw new ArgumentNullException(nameof(sourceProviderFactory));
        _outputSinkFactory = outputSinkFactory ?? throw new ArgumentNullException(nameof(outputSinkFactory));
        _encodedOutputRouteFactory = encodedOutputRouteFactory;
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        _externalDiagnostics = diagnostics;
        _diagnostics = new EngineDiagnosticsSink(diagnostics, RaiseDiagnosticReported);
        _sinkDispatcher = new RenderOutputSinkDispatcher(_diagnostics, _sinkStopTimeout);
        _outputRouteTransitions.PhaseChanged += OnOutputRouteTransitionPhaseChanged;
    }

    public bool HasProject => _currentProject is not null;

    public MediaForgeProject? CurrentProject => _projectCoordinator.CreatePublicSnapshot();

    public MediaForgeEngineState State => _lifecycleCoordinator.State;

    public bool IsRunning => State == MediaForgeEngineState.Running;

    public event EventHandler<MediaForgeDiagnosticEventArgs>? DiagnosticReported;

    public event EventHandler<MediaForgeEngineStateChangedEventArgs>? StateChanged;

    public event EventHandler<MediaForgeFrameDroppedEventArgs>? FrameDropped;

    public event EventHandler<MediaForgeRecoveryEventArgs>? RecoveryStateChanged;

    public event EventHandler<OutputSceneTransitionEventArgs>? OutputSceneTransitionStateChanged;

    public MediaForgeRuntimeHealthSnapshot GetRuntimeHealthSnapshot()
    {
        var outputs = GetEncodedOutputRuntimeSnapshots();
        var internalRecoveries = _faultRecoveryCoordinator?.States.Values.ToArray() ?? [];
        var recoveries = internalRecoveries.Select(ToPublicRecoverySnapshot).ToArray();
        var backendResources = (_backend as IRenderBackendResourceDiagnostics)?.GetResourceSnapshot();
        var status = EngineHealthCoordinator.ResolveStatus(
            State,
            internalRecoveries,
            outputs,
            backendResources?.FailedRetiredResources ?? 0);

        return new MediaForgeRuntimeHealthSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Status = status,
            EngineState = State,
            EncodedOutputs = outputs,
            Recoveries = recoveries,
            SceneVersions = _sceneRuntime?.VersionRetentionSnapshot ?? new SceneVersionRetentionSnapshot(),
            GpuResources = new MediaForgeGpuResourceHealthSnapshot
            {
                PendingSubmissions = _renderThread?.PendingTracker.PendingCount ?? 0,
                ExternalTextureImports = backendResources?.ExternalTextureImports ?? 0,
                BoundOutputTargets = backendResources?.BoundOutputTargets ?? 0,
                CachedIntermediateTargets = backendResources?.CachedIntermediateTargets ?? 0,
                ActiveIntermediateBorrows = backendResources?.ActiveIntermediateBorrows ?? 0,
                RetiredIntermediateTargets = backendResources?.RetiredIntermediateTargets ?? 0,
                ActivePooledTextures = backendResources?.ActivePooledTextures ?? 0,
                AvailablePooledTextures = backendResources?.AvailablePooledTextures ?? 0,
                PendingFenceTextures = backendResources?.PendingFenceTextures ?? 0,
                PendingRetiredResources = backendResources?.PendingRetiredResources ?? 0,
                FailedRetiredResources = backendResources?.FailedRetiredResources ?? 0,
                LiveFramebuffers = backendResources?.LiveFramebuffers ?? 0,
                LiveDescriptorSets = backendResources?.LiveDescriptorSets ?? 0,
                FramebufferHighWaterMark = backendResources?.FramebufferHighWaterMark ?? 0,
                DescriptorSetHighWaterMark = backendResources?.DescriptorSetHighWaterMark ?? 0,
                PooledTextureHighWaterMark = backendResources?.PooledTextureHighWaterMark ?? 0,
                IntermediateTargetHighWaterMark = backendResources?.IntermediateTargetHighWaterMark ?? 0
            }
        };
    }

    public IReadOnlyList<EncodedOutputRuntimeSnapshot> GetEncodedOutputRuntimeSnapshots() =>
        _mediaPipelineRuntime?.GetEncodedOutputRuntimeSnapshots()
        ?? Array.Empty<EncodedOutputRuntimeSnapshot>();

    public async Task StartEncodedOutputAsync(
        RenderOutputId outputId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureEncodedOutputControlAvailable(outputId);
            if (_mediaPipelineRuntime!.IsEncodedOutputRegistered(outputId))
                return;

            var workingProject = MediaForgeProjectCloner.DeepClone(_currentProject!);
            var output = workingProject.Outputs.Single(candidate => candidate.Id == outputId);
            output.Enabled = true;
            MediaForgeProjectValidator.Validate(workingProject).ThrowIfInvalid();

            var surfaceOutputId = _encodedOutputRouteFactory!.ResolveSurfaceOutputId(workingProject, output);
            var surfaceOutput = workingProject.Outputs.Single(candidate => candidate.Id == surfaceOutputId);
            var createdBinding = await EnsureAutomaticSurfaceBindingAsync(surfaceOutput, cancellationToken).ConfigureAwait(false);
            try
            {
                await _encodedOutputRouteFactory.RegisterAsync(
                    workingProject,
                    output,
                    _mediaPipelineRuntime,
                    cancellationToken).ConfigureAwait(false);
                _currentProject = workingProject;
                RefreshPublishedRuntimeAfterProjectMutation();
            }
            catch
            {
                if (createdBinding)
                    await RemoveSurfaceBindingAsync(surfaceOutputId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopEncodedOutputAsync(
        RenderOutputId outputId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureEncodedOutputControlAvailable(outputId);
            var output = _currentProject!.Outputs.Single(candidate => candidate.Id == outputId);
            if (!_mediaPipelineRuntime!.TryGetSurfaceOutputId(outputId, out var surfaceOutputId))
                return;

            Exception? finalizationFailure = null;
            try
            {
                await _encodedOutputRouteFactory!.UnregisterAsync(
                    output,
                    _mediaPipelineRuntime,
                    SinkStopTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                finalizationFailure = exception;
            }

            var workingProject = MediaForgeProjectCloner.DeepClone(_currentProject);
            workingProject.Outputs.Single(candidate => candidate.Id == outputId).Enabled = false;
            _currentProject = workingProject;
            RefreshPublishedRuntimeAfterProjectMutation();

            if (!_mediaPipelineRuntime.GetActiveSurfaceOutputIds().Contains(surfaceOutputId))
                await RemoveSurfaceBindingAsync(surfaceOutputId, CancellationToken.None).ConfigureAwait(false);

            if (finalizationFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(finalizationFailure).Throw();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal SceneRuntime? SceneRuntimeForTests => _sceneRuntime;

    internal OutputRouteTransitionRuntime OutputRouteTransitionRuntimeForTests => _outputRouteTransitions;

    internal FaultRecoveryCoordinator? FaultRecoveryCoordinatorForTests => _faultRecoveryCoordinator;

    internal CompositionRuntime? RuntimeForTests => _runtime;

    internal MediaPipelineRuntime? MediaPipelineRuntimeForTests => _mediaPipelineRuntime;

    internal MediaForgeRenderThread? RenderThreadForTests => _renderThread;

    internal FrameScheduler? FrameSchedulerForTests => _renderPump?.Scheduler;

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
            _sceneEditSessions.Clear();
            _outputRouteTransitions.Clear();
            RefreshPublishedRuntimeAfterProjectMutation(requestFrame: false);
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

                _recoveryCancellation = new CancellationTokenSource();
                _faultRecoveryCoordinator = new FaultRecoveryCoordinator(_diagnostics);
                _faultRecoveryCoordinator.RecoveryStateChanged += OnRecoveryStateChanged;

                _sourceRuntimeManager = new SourceRuntimeManager(_diagnostics);
                _runtime = new CompositionRuntime(_sourceRuntimeManager);
                _mediaPipelineRuntime = new MediaPipelineRuntime(_diagnostics);
                _renderThreadGuard = new RenderThreadGuard();

                if (!_backendFactory.TryCreate(_renderThreadGuard, _diagnostics, out var backend) || backend is null)
                    throw CreateEngineException("Render backend could not be created.");

                _backend = backend;
                _renderThread = new MediaForgeRenderThread(
                    _backend,
                    _renderThreadGuard,
                    diagnostics: _diagnostics,
                    sinkDispatcher: _sinkDispatcher,
                    outputFrameConsumers: [_mediaPipelineRuntime],
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

                RefreshPublishedRuntimeAfterProjectMutation(requestFrame: false);
                _renderThread.Start();

                await EnsureSurfaceBindingsForAttachedSinksAsync(_currentProject, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (outputId, entry) in _outputSinks)
                {
                    var output = _currentProject.Outputs.First(o => o.Id == outputId);
                    if (!output.Enabled &&
                        !(_mediaPipelineRuntime?.GetActiveSurfaceOutputIds().Contains(outputId) ?? false))
                        continue;
                    await EnqueueBindOutputAsync(output, entry.Sink, entry.Target, cancellationToken)
                        .ConfigureAwait(false);
                }

                await RegisterEncodedOutputRoutesAsync(_currentProject, cancellationToken)
                    .ConfigureAwait(false);

                _renderPump = new MediaForgeRenderPump(
                    RenderFramesPerSecond,
                    CanPublishRenderFrame,
                    PublishScheduledRenderFrame,
                    GetScheduledTargetOutputs,
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

            RefreshPublishedRuntimeAfterProjectMutation();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
        CanvasId canvasId,
        SceneEditMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();
            if (mode is not (SceneEditMode.Live or SceneEditMode.Apply))
                throw CreateEngineException($"Unsupported scene edit mode '{mode}'.");

            EnsureCanvasExists(_currentProject!, canvasId);

            var sessionId = SceneEditSessionId.New();
            var baseVersion = GetPublishedSceneVersion(canvasId);
            var now = DateTimeOffset.UtcNow;
            SceneVersionId? draftVersion = mode == SceneEditMode.Apply
                ? SceneVersionId.New()
                : null;

            var descriptor = new SceneEditSessionDescriptor
            {
                SessionId = sessionId,
                CanvasId = canvasId,
                Mode = mode,
                BasePublishedVersionId = baseVersion,
                DraftVersionId = draftVersion,
                CreatedAt = now
            };

            MediaForgeProject? draftProject = null;
            if (mode == SceneEditMode.Apply)
            {
                draftProject = MediaForgeProjectCloner.DeepClone(_currentProject!);
                UpsertDraftRuntimeState(descriptor, draftProject, hasChanges: false);
            }

            _sceneEditSessions.Add(
                sessionId,
                new ActiveSceneEditSession(
                    descriptor,
                    draftProject,
                    HasChanges: false));

            return descriptor;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ApplySceneMutationAsync(
        SceneEditSessionId sessionId,
        SceneMutationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await ApplySceneMutationsAsync(sessionId, [patch], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ApplySceneMutationsAsync(
        SceneEditSessionId sessionId,
        IReadOnlyList<SceneMutationPatch> patches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patches);
        if (patches.Count == 0)
            return;

        var patchBatch = patches.ToArray();
        if (patchBatch.Any(static patch => patch is null))
            throw new ArgumentException("Scene mutation batches cannot contain null patches.", nameof(patches));

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();
            var session = RequireSceneEditSession(sessionId);

            if (session.Descriptor.Mode == SceneEditMode.Live)
            {
                var workingCopy = MediaForgeProjectCloner.DeepClone(_currentProject!);
                foreach (var patch in patchBatch)
                    SceneMutationPatchApplier.Apply(workingCopy, session.Descriptor.CanvasId, patch);
                MediaForgeProjectValidator.Validate(workingCopy).ThrowIfInvalid();

                _currentProject = workingCopy;
                RefreshPublishedRuntimeAfterProjectMutation();
                ReplaceSceneEditSession(session with { HasChanges = true });
                return;
            }

            var draftProject = session.DraftProject
                ?? throw CreateEngineException("Apply-mode scene edit session does not have a draft project.");

            var draftWorkingCopy = MediaForgeProjectCloner.DeepClone(draftProject);
            foreach (var patch in patchBatch)
                SceneMutationPatchApplier.Apply(draftWorkingCopy, session.Descriptor.CanvasId, patch);
            MediaForgeProjectValidator.Validate(draftWorkingCopy).ThrowIfInvalid();

            var updated = session with
            {
                DraftProject = draftWorkingCopy,
                HasChanges = true
            };

            ReplaceSceneEditSession(updated);
            UpsertDraftRuntimeState(updated.Descriptor, draftWorkingCopy, hasChanges: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<SceneCommitResult> ApplySceneDraftAsync(
        SceneEditSessionId sessionId,
        SceneCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();
            ValidateSceneCommitRequest(request);
            var session = RequireSceneEditSession(sessionId);
            if (session.Descriptor.Mode != SceneEditMode.Apply)
                throw CreateEngineException("Only Apply-mode scene edit sessions can be committed.");

            var draftProject = session.DraftProject
                ?? throw CreateEngineException("Apply-mode scene edit session does not have a draft project.");

            var currentVersion = GetPublishedSceneVersion(session.Descriptor.CanvasId);
            if (!request.AllowStaleBase && currentVersion != session.Descriptor.BasePublishedVersionId)
            {
                throw CreateEngineException(
                    $"Scene draft is stale. Base version {session.Descriptor.BasePublishedVersionId} no longer matches published version {currentVersion}.");
            }

            var committedCanvas = EnsureCanvasExists(draftProject, session.Descriptor.CanvasId);
            var workingCopy = MediaForgeProjectCloner.DeepClone(_currentProject!);
            ReplaceCanvas(workingCopy, committedCanvas);
            MediaForgeProjectValidator.Validate(workingCopy).ThrowIfInvalid();

            var oldVersion = currentVersion;
            var previousProjectState = _sceneRuntime?.CreateSnapshot().ProjectState
                ?? _projectState
                ?? ProjectStateSnapshotFactory.CreateImmutableSnapshot(_currentProject!);
            var previousVersionMap = CreateCurrentCanvasVersionMap();
            _sceneRuntime?.DiscardDraft(sessionId);
            _sceneEditSessions.Remove(sessionId);

            _currentProject = workingCopy;
            RefreshPublishedRuntimeAfterProjectMutation(requestFrame: false);
            var newVersion = GetPublishedSceneVersion(session.Descriptor.CanvasId);
            var currentVersionMap = CreateCurrentCanvasVersionMap();

            var graph = SceneDependencyGraphBuilder.Build(_currentProject);
            SceneDependencyGraphValidator.Validate(graph).ThrowIfInvalid();
            var propagation = new SceneCommitPropagationPlanner(graph)
                .Plan(session.Descriptor.CanvasId, oldVersion, newVersion, request.TransitionPolicy);
            BeginOutputRouteTransitions(
                propagation,
                graph,
                previousVersionMap,
                currentVersionMap,
                previousProjectState);

            _renderPump?.RequestFrame();

            return new SceneCommitResult
            {
                SessionId = sessionId,
                CanvasId = session.Descriptor.CanvasId,
                OldVersionId = oldVersion,
                NewVersionId = newVersion,
                AffectedCanvases = propagation.AffectedCanvases.AllAffected,
                AffectedOutputs = propagation.AffectedOutputs.OutputRouteIds,
                TransitionRequested = propagation.UsesTransition
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyDictionary<CanvasId, SceneVersionId> CreateCurrentCanvasVersionMap()
    {
        var publishedVersions = _sceneRuntime?.PublishedStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.VersionId);

        if (publishedVersions is { Count: > 0 })
            return publishedVersions;

        return _projectState?.CanvasVersionIds.ToDictionary(static pair => pair.Key, static pair => pair.Value)
            ?? new Dictionary<CanvasId, SceneVersionId>();
    }

    private void BeginOutputRouteTransitions(
        SceneCommitPropagationPlan propagation,
        SceneDependencyGraph graph,
        IReadOnlyDictionary<CanvasId, SceneVersionId> previousVersionMap,
        IReadOnlyDictionary<CanvasId, SceneVersionId> currentVersionMap,
        ProjectStateSnapshot previousProjectState)
    {
        ArgumentNullException.ThrowIfNull(previousProjectState);

        if (_currentProject is null || !propagation.UsesTransition)
            return;

        foreach (var outputId in propagation.AffectedOutputs.OutputRouteIds)
        {
            var output = _currentProject.Outputs.FirstOrDefault(candidate => candidate.Id == outputId);
            if (output is null)
                continue;

            var transition = ResolveApplyTransition(output, propagation.TransitionPolicy);
            if (transition.Kind == OutputRouteTransitionKind.Cut)
                continue;

            var previousGraph = CreateSceneVersionGraph(output.CanvasId, previousVersionMap, graph);
            var currentGraph = CreateSceneVersionGraph(output.CanvasId, currentVersionMap, graph);
            var versionOwnership = _sceneRuntime!.PinVersionGraphs(
                previousGraph,
                currentGraph,
                $"transition:{output.Id}");

            try
            {
                _outputRouteTransitions.BeginSceneVersionTransition(
                    output.Id,
                    transition,
                    previousGraph,
                    currentGraph,
                    previousProjectState,
                    versionOwnership);
            }
            catch
            {
                versionOwnership.Dispose();
                throw;
            }
        }
    }

    private static OutputRouteTransition ResolveApplyTransition(
        MediaForgeRenderOutput output,
        SceneApplyTransitionPolicy policy)
    {
        return policy.Kind switch
        {
            SceneApplyTransitionKind.UseOutputRoutePolicy => output.RouteTransition,
            SceneApplyTransitionKind.Cut => OutputRouteTransition.Cut("apply-cut", "Apply cut"),
            SceneApplyTransitionKind.Fade => OutputRouteTransition.Fade(
                "apply-fade",
                durationMs: Math.Max(1, (int)Math.Ceiling(policy.Duration.TotalMilliseconds)),
                displayName: "Apply fade"),
            _ => throw CreateInvalidTransitionPolicyException(policy)
        };
    }

    private static InvalidOperationException CreateInvalidTransitionPolicyException(SceneApplyTransitionPolicy policy) =>
        new($"Unsupported scene apply transition kind '{policy.Kind}'.");

    private static SceneVersionGraph CreateSceneVersionGraph(
        CanvasId rootCanvasId,
        IReadOnlyDictionary<CanvasId, SceneVersionId> versionMap,
        SceneDependencyGraph dependencyGraph)
    {
        var versions = new Dictionary<CanvasId, SceneVersionId>();
        var visited = new HashSet<CanvasId>();
        var stack = new Stack<CanvasId>();
        stack.Push(rootCanvasId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (versionMap.TryGetValue(current, out var version))
                versions[current] = version;

            foreach (var nested in dependencyGraph.GetNestedCanvases(current))
                stack.Push(nested);
        }

        return new SceneVersionGraph(rootCanvasId, versions);
    }

    public async ValueTask DiscardSceneDraftAsync(
        SceneEditSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = RequireSceneEditSession(sessionId);
            if (session.Descriptor.Mode == SceneEditMode.Apply)
                _sceneRuntime?.DiscardDraft(sessionId);

            _sceneEditSessions.Remove(sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OutputSceneTransitionResult> TransitionOutputToSceneAsync(
        RenderOutputId outputId,
        CanvasId destinationCanvasId,
        SceneVersionBinding destinationBinding,
        OutputRouteTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        destinationBinding.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutateProject();
            EnsureSceneRuntimeCurrent();
            EnsureCanvasExists(_currentProject!, destinationCanvasId);

            var sourceOutput = _currentProject!.Outputs.FirstOrDefault(candidate => candidate.Id == outputId)
                ?? throw CreateEngineException($"Output {outputId} was not found in the current project.");
            ValidateRouteTransition(transition);

            var previousProjectState = _sceneRuntime!.CreateSnapshot().ProjectState;
            var destinationProject = MediaForgeProjectCloner.DeepClone(_currentProject);
            var destinationOutput = destinationProject.Outputs.Single(candidate => candidate.Id == outputId);
            destinationOutput.CanvasId = destinationCanvasId;
            destinationOutput.SceneVersionBinding = destinationBinding;
            MediaForgeProjectValidator.Validate(destinationProject).ThrowIfInvalid();

            var destinationProjectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(destinationProject) with
            {
                CanvasVersionIds = previousProjectState.CanvasVersionIds,
                CanvasVersionSnapshots = previousProjectState.CanvasVersionSnapshots
            };
            destinationProjectState = _sceneRuntime.ResolveOutputVersionBindings(destinationProjectState);

            var graph = SceneDependencyGraphBuilder.Build(_currentProject);
            SceneDependencyGraphValidator.Validate(graph).ThrowIfInvalid();
            var previousGraph = CreateSceneVersionGraph(
                sourceOutput.CanvasId,
                ResolveOutputVersionMap(previousProjectState, outputId),
                graph);
            var currentGraph = CreateSceneVersionGraph(
                destinationCanvasId,
                ResolveOutputVersionMap(destinationProjectState, outputId),
                graph);
            var versionOwnership = _sceneRuntime.PinVersionGraphs(
                previousGraph,
                currentGraph,
                $"route-transition:{outputId}");
            var operationId = Guid.NewGuid();
            var active = new ActiveOutputSceneTransition(
                operationId,
                outputId,
                sourceOutput.CanvasId,
                destinationCanvasId,
                destinationBinding);

            lock (_sceneRouteTransitionGate)
                _sceneRouteTransitions.Add(operationId, active);

            try
            {
                _outputRouteTransitions.BeginSceneRouteTransition(
                    operationId,
                    outputId,
                    transition,
                    previousGraph,
                    currentGraph,
                    previousProjectState,
                    destinationProjectState,
                    versionOwnership);
                active.CancellationRegistration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation = (OutputSceneTransitionCancellation)state!;
                        cancellation.Runtime.Cancel(cancellation.OutputId, cancellation.OperationId);
                    },
                    new OutputSceneTransitionCancellation(_outputRouteTransitions, outputId, operationId));
            }
            catch
            {
                lock (_sceneRouteTransitionGate)
                    _sceneRouteTransitions.Remove(operationId);
                versionOwnership.Dispose();
                active.Dispose();
                throw;
            }

            _renderPump?.RequestFrame();
            return new OutputSceneTransitionResult(
                operationId,
                outputId,
                sourceOutput.CanvasId,
                destinationCanvasId,
                destinationBinding,
                transition);
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
            if (!output.Enabled)
                throw CreateEngineException($"Output {outputId} is disabled and cannot be bound.");

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
            if (!output.Enabled)
                throw CreateEngineException($"Output {outputId} is disabled and cannot accept sinks.");

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
        if (!_lifecycleCoordinator.TryBeginDispose())
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

                _outputRouteTransitions.Dispose();

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
            _lifecycleCoordinator.Dispose();
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

    private IReadOnlyList<RenderOutputId> GetScheduledTargetOutputs()
    {
        var enabled = _currentProject?.Outputs
            .Where(static output => output.Enabled)
            .Select(static output => output.Id)
            ?? [];
        var encodedSurfaces = _mediaPipelineRuntime?.GetActiveSurfaceOutputIds() ?? [];
        return enabled.Concat(encodedSurfaces).Distinct().ToArray();
    }

    private void EnsureEncodedOutputControlAvailable(RenderOutputId outputId)
    {
        if (outputId.IsEmpty)
            throw CreateEngineException("Encoded output id cannot be empty.");
        if (State != MediaForgeEngineState.Running)
            throw CreateEngineException("Encoded outputs can only be controlled while the engine is running.");
        if (_currentProject is null || _mediaPipelineRuntime is null || _encodedOutputRouteFactory is null)
            throw CreateEngineException("Encoded output runtime is unavailable.");
        var output = _currentProject.Outputs.FirstOrDefault(candidate => candidate.Id == outputId)
            ?? throw CreateEngineException($"Encoded output {outputId} was not found.");
        if (!_encodedOutputRouteFactory.CanCreate(output.TypeId))
            throw new MediaForgeUnsupportedFeatureException(
                $"output.{output.TypeId.Value}",
                $"Output '{output.Name}' is not a controllable encoded route.");
    }

    private void PublishScheduledRenderFrame(FrameExecutionContext executionContext)
    {
        if (_runtime is null || _sceneRuntime is null || _renderThread is null)
            return;

        var context = new RenderFrameContext(
            executionContext.FrameId,
            executionContext.PresentationTime,
            executionContext.FrameBudget,
            RenderFramesPerSecond,
            CancellationToken.None);

        _outputRouteTransitions.AdvanceAll(executionContext.FrameBudget);
        var sceneSnapshot = _sceneRuntime.CreateSnapshot();
        using var buildResult = _sceneRuntime.BuildRenderSnapshot(
            _runtime,
            context,
            _outputRouteTransitions,
            _diagnostics);
        var snapshot = buildResult.TakeSnapshot();

        if (snapshot is null)
            return;

        try
        {
            var renderGraphPlan = MediaForgeRenderGraphCompiler.Compile(snapshot);

            snapshot.RenderGraphExecution = RenderGraphExecutor.Execute(
                renderGraphPlan,
                new RenderGraphContext
                {
                    FrameContext = executionContext,
                    SceneSnapshot = sceneSnapshot,
                    SourceFrames = CreateSourceFrameMap(snapshot)
                });

            _renderThread.PublishFrame(snapshot);
            snapshot = null;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    private static IReadOnlyDictionary<SourceId, GpuFrameReference> CreateSourceFrameMap(
        RenderFrameSnapshot snapshot)
    {
        var sourceFrames = new Dictionary<SourceId, GpuFrameReference>();
        foreach (var lease in snapshot.FrameLeases)
            sourceFrames.TryAdd(lease.Frame.SourceId, lease.Frame);

        return sourceFrames;
    }

    private async Task EnsureSurfaceBindingsForAttachedSinksAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken)
    {
        foreach (var output in project.Outputs)
        {
            if (!output.Enabled)
                continue;
            if (_sinkDispatcher.HasSinks(output.Id))
                await EnsureAutomaticSurfaceBindingAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RegisterEncodedOutputRoutesAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken)
    {
        if (_encodedOutputRouteFactory is null || _mediaPipelineRuntime is null)
            return;

        foreach (var output in project.Outputs)
        {
            if (!output.Enabled)
                continue;
            if (!_encodedOutputRouteFactory.CanCreate(output.TypeId))
                continue;

            var surfaceOutputId = _encodedOutputRouteFactory.ResolveSurfaceOutputId(project, output);
            if (surfaceOutputId != output.Id)
            {
                await _encodedOutputRouteFactory
                    .RegisterAsync(project, output, _mediaPipelineRuntime, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var createdBinding = await EnsureAutomaticSurfaceBindingAsync(output, cancellationToken).ConfigureAwait(false);
            if (createdBinding && _outputSinks.TryGetValue(output.Id, out var entry))
            {
                await EnqueueBindOutputAsync(output, entry.Sink, entry.Target, cancellationToken)
                    .ConfigureAwait(false);
            }

            await _encodedOutputRouteFactory
                .RegisterAsync(project, output, _mediaPipelineRuntime, cancellationToken)
                .ConfigureAwait(false);
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

    private ActiveSceneEditSession RequireSceneEditSession(SceneEditSessionId sessionId)
    {
        if (sessionId.IsEmpty)
            throw CreateEngineException("Scene edit session id cannot be empty.");

        return _sceneEditSessionCoordinator.TryGet(sessionId, out var session)
            ? session
            : throw CreateEngineException($"Scene edit session {sessionId} was not found.");
    }

    private void ReplaceSceneEditSession(ActiveSceneEditSession session) =>
        _sceneEditSessionCoordinator.Replace(session.Descriptor.SessionId, session);

    private SceneVersionId GetPublishedSceneVersion(CanvasId canvasId)
    {
        EnsureSceneRuntimeCurrent();
        return _sceneRuntime!.GetPublishedVersion(canvasId);
    }

    private void EnsureSceneRuntimeCurrent()
    {
        if (_currentProject is null)
            throw CreateEngineException("A project must be loaded before scene runtime can be used.");

        if (_sceneRuntime is not null && _projectState is not null)
            return;

        RefreshPublishedRuntimeAfterProjectMutation(requestFrame: false);
    }

    private void RefreshPublishedRuntimeAfterProjectMutation(bool requestFrame = true)
    {
        if (_currentProject is null)
            return;

        _projectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(_currentProject);
        _sceneRuntime ??= new SceneRuntime();
        _sceneRuntime.SyncFrom(_projectState);

        foreach (var session in _sceneEditSessions.Values)
        {
            if (session.Descriptor.Mode == SceneEditMode.Apply && session.DraftProject is not null)
                UpsertDraftRuntimeState(session.Descriptor, session.DraftProject, session.HasChanges);
        }

        if (requestFrame && State == MediaForgeEngineState.Running)
            _renderPump?.RequestFrame();
    }

    private void OnOutputRouteTransitionPhaseChanged(
        object? sender,
        OutputRouteTransitionPhaseChangedEventArgs args)
    {
        ActiveOutputSceneTransition? active;
        lock (_sceneRouteTransitionGate)
            _sceneRouteTransitions.TryGetValue(args.OperationId, out active);
        if (active is null)
            return;

        lock (active.SyncRoot)
        {
            active.PhaseTail = active.PhaseTail
                .ContinueWith(
                    _ => HandleOutputSceneTransitionPhaseAsync(active, args),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task HandleOutputSceneTransitionPhaseAsync(
        ActiveOutputSceneTransition active,
        OutputRouteTransitionPhaseChangedEventArgs args)
    {
        if (args.Phase == OutputRouteTransitionPhase.SwitchPointReached)
        {
            try
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (State == MediaForgeEngineState.Disposed || _currentProject is null)
                        throw CreateEngineException("The engine stopped before the scene route reached its switch point.");

                    var output = _currentProject.Outputs.FirstOrDefault(candidate => candidate.Id == active.OutputId)
                        ?? throw CreateEngineException($"Output {active.OutputId} was removed during its scene transition.");
                    output.CanvasId = active.DestinationCanvasId;
                    output.SceneVersionBinding = active.DestinationBinding;
                    RefreshPublishedRuntimeAfterProjectMutation();
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                active.TerminalFailure = ex;
                RaiseOutputSceneTransitionStateChanged(active, OutputSceneTransitionStatus.Failed, args.Progress, ex);
                RemoveOutputSceneTransition(active);
                return;
            }
        }

        if (active.TerminalFailure is not null)
            return;

        var status = args.Phase switch
        {
            OutputRouteTransitionPhase.Started => OutputSceneTransitionStatus.Started,
            OutputRouteTransitionPhase.SwitchPointReached => OutputSceneTransitionStatus.SwitchPointReached,
            OutputRouteTransitionPhase.Completed => OutputSceneTransitionStatus.Completed,
            OutputRouteTransitionPhase.Cancelled => OutputSceneTransitionStatus.Cancelled,
            OutputRouteTransitionPhase.Failed => OutputSceneTransitionStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(args))
        };
        RaiseOutputSceneTransitionStateChanged(active, status, args.Progress, args.Failure);

        if (status is OutputSceneTransitionStatus.Completed or OutputSceneTransitionStatus.Cancelled or OutputSceneTransitionStatus.Failed)
            RemoveOutputSceneTransition(active);
    }

    private void RaiseOutputSceneTransitionStateChanged(
        ActiveOutputSceneTransition active,
        OutputSceneTransitionStatus status,
        float progress,
        Exception? failure) =>
        SafeRaiseEvent(
            nameof(OutputSceneTransitionStateChanged),
            () => OutputSceneTransitionStateChanged?.Invoke(
                this,
                new OutputSceneTransitionEventArgs
                {
                    OperationId = active.OperationId,
                    OutputId = active.OutputId,
                    SourceCanvasId = active.SourceCanvasId,
                    DestinationCanvasId = active.DestinationCanvasId,
                    Status = status,
                    Progress = progress,
                    Failure = failure
                }));

    private void RemoveOutputSceneTransition(ActiveOutputSceneTransition active)
    {
        lock (_sceneRouteTransitionGate)
            _sceneRouteTransitions.Remove(active.OperationId);
        active.Dispose();
    }

    private static IReadOnlyDictionary<CanvasId, SceneVersionId> ResolveOutputVersionMap(
        ProjectStateSnapshot projectState,
        RenderOutputId outputId) =>
        projectState.ResolvedOutputCanvases.TryGetValue(outputId, out var resolved)
            ? resolved.CanvasVersionIds
            : projectState.CanvasVersionIds;

    private static void ValidateRouteTransition(OutputRouteTransition transition)
    {
        if (string.IsNullOrWhiteSpace(transition.Id))
            throw new ArgumentException("A scene route transition id is required.", nameof(transition));
        if (transition.Kind == OutputRouteTransitionKind.Cut && transition.DurationMs != 0)
            throw new ArgumentException("A cut transition must have zero duration.", nameof(transition));
        if (transition.Kind == OutputRouteTransitionKind.Fade && transition.DurationMs <= 0)
            throw new ArgumentException("A fade transition must have a positive duration.", nameof(transition));
    }

    private void UpsertDraftRuntimeState(
        SceneEditSessionDescriptor descriptor,
        MediaForgeProject draftProject,
        bool hasChanges)
    {
        EnsureSceneRuntimeCurrent();

        var draftVersion = descriptor.DraftVersionId
            ?? throw CreateEngineException("Apply-mode scene edit session does not have a draft version id.");

        var draftProjectState = ProjectStateSnapshotFactory.CreateImmutableSnapshot(draftProject);
        _sceneRuntime!.UpsertDraft(
            new SceneDraftState
            {
                SessionId = descriptor.SessionId,
                CanvasId = descriptor.CanvasId,
                BasePublishedVersionId = descriptor.BasePublishedVersionId,
                DraftVersionId = draftVersion,
                HasChanges = hasChanges
            },
            draftProjectState);
    }

    private MediaForgeCanvas EnsureCanvasExists(MediaForgeProject project, CanvasId canvasId) =>
        project.Canvases.FirstOrDefault(canvas => canvas.Id == canvasId)
        ?? throw CreateEngineException($"Canvas {canvasId} was not found in the current project.");

    private static void ReplaceCanvas(MediaForgeProject project, MediaForgeCanvas committedCanvas)
    {
        var index = project.Canvases.FindIndex(canvas => canvas.Id == committedCanvas.Id);
        if (index < 0)
            throw new InvalidOperationException($"Canvas {committedCanvas.Id} was not found in the target project.");

        project.Canvases[index] = committedCanvas;
    }

    private static void ValidateSceneCommitRequest(SceneCommitRequest request)
    {
        if (request.TransitionPolicy is null)
            throw new ArgumentException("Scene commit transition policy cannot be null.", nameof(request));

        switch (request.TransitionPolicy.Kind)
        {
            case SceneApplyTransitionKind.UseOutputRoutePolicy:
            case SceneApplyTransitionKind.Cut:
                if (request.TransitionPolicy.Duration < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        "Cut and output-route transition policies cannot have a negative duration.");
                }

                break;

            case SceneApplyTransitionKind.Fade:
                if (request.TransitionPolicy.Duration <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        "Fade transition duration must be positive.");
                }

                break;

            default:
                throw CreateInvalidTransitionPolicyException(request.TransitionPolicy);
        }
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
            _mediaPipelineRuntime is null &&
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

        await StopActiveRecoveriesAsync(cleanupErrors).ConfigureAwait(false);

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

        var mediaPipelineRuntime = _mediaPipelineRuntime;
        _mediaPipelineRuntime = null;

        if (mediaPipelineRuntime is not null)
        {
            try
            {
                await mediaPipelineRuntime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.media_pipeline_dispose_failed",
                    "Failed to dispose media pipeline runtime during engine cleanup.",
                    nameof(MediaForgeEngine),
                    ex);
            }
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

        if (_sourceRuntimeManager is not null)
        {
            try
            {
                await _sourceRuntimeManager.ClearAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.source_runtime_dispose_failed",
                    "Failed to dispose source runtimes during engine cleanup.",
                    nameof(MediaForgeEngine),
                    ex);
            }
        }

        _sourceRuntimeManager = null;
        _runtime = null;
        _sceneRuntime = null;
        if (_faultRecoveryCoordinator is not null)
            _faultRecoveryCoordinator.RecoveryStateChanged -= OnRecoveryStateChanged;
        _faultRecoveryCoordinator = null;
        _projectState = null;
        _outputRouteTransitions.Clear();

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

        _lifecycleCoordinator.SetState(newState);
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
        ObjectDisposedException.ThrowIf(_lifecycleCoordinator.IsDisposed, this);

    private void RaiseDiagnosticReported(MediaForgeDiagnostic diagnostic)
    {
        SafeRaiseEvent(
            nameof(DiagnosticReported),
            () => DiagnosticReported?.Invoke(this, new MediaForgeDiagnosticEventArgs(diagnostic)));

        if (diagnostic.Code is "render.frame_dropped_tracker_full" or
            "engine.render_pump_frame_dropped_backpressure" or
            "engine.frame_scheduler_frame_dropped_backpressure" or
            "sink.frame_dropped_backpressure")
        {
            SafeRaiseEvent(
                nameof(FrameDropped),
                () => FrameDropped?.Invoke(this, new MediaForgeFrameDroppedEventArgs(diagnostic)));
        }

        if (diagnostic.Code == "source.frame_acquire_failed" && diagnostic.SourceId is Guid sourceId)
        {
            ScheduleSourceRecovery(SourceId.From(sourceId), diagnostic.Message);
        }
        else if (diagnostic.Code == "render.submit_failed")
        {
            ScheduleGraphicsDeviceRecovery(diagnostic.Exception?.Message ?? diagnostic.Message);
        }
        else if (diagnostic.Code.StartsWith("engine.encode_scheduler_", StringComparison.Ordinal) &&
                 diagnostic.Severity >= MediaForgeDiagnosticSeverity.Error)
        {
            if (diagnostic.OutputId is Guid outputId)
            {
                ScheduleEncodedOutputRecovery(
                    RenderOutputId.From(outputId),
                    diagnostic.Exception?.Message ?? diagnostic.Message);
            }
            else
            {
                _faultRecoveryCoordinator?.NotifyEncoderUnavailable(
                    diagnostic.Exception?.Message ?? diagnostic.Message);
            }
        }
        else if (diagnostic.Code.StartsWith("engine.encoding_pipeline_", StringComparison.Ordinal) &&
                 diagnostic.Severity >= MediaForgeDiagnosticSeverity.Error &&
                 diagnostic.OutputId is Guid pipelineOutputId)
        {
            ScheduleEncodedOutputRecovery(
                RenderOutputId.From(pipelineOutputId),
                diagnostic.Exception?.Message ?? diagnostic.Message);
        }
        else if (diagnostic.Code == "engine.encoded_router_consumer_failed" &&
                 diagnostic.Message.Contains("Rtmp", StringComparison.OrdinalIgnoreCase))
        {
            _faultRecoveryCoordinator?.NotifyRtmpDisconnected(diagnostic.Exception?.Message ?? diagnostic.Message);
        }
    }

    private void ScheduleSourceRecovery(SourceId sourceId, string detail)
    {
        var coordinator = _faultRecoveryCoordinator;
        var cancellation = _recoveryCancellation;
        if (coordinator is null || cancellation is null || cancellation.IsCancellationRequested)
            return;

        var key = $"source:{sourceId}";
        _engineRecoveryCoordinator.TryStart(
            key,
            () => RunSourceRecoveryAsync(coordinator, sourceId, key, detail, cancellation.Token),
            completed =>
            {
                if (completed.IsFaulted)
                {
                    MediaForgeDiagnostics.Report(
                        _externalDiagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.source_recovery_failed",
                        $"Automatic recovery failed for source {sourceId}.",
                        nameof(MediaForgeEngine),
                        completed.Exception?.GetBaseException());
                }
            });
    }

    private void ScheduleGraphicsDeviceRecovery(string detail)
    {
        var coordinator = _faultRecoveryCoordinator;
        var cancellation = _recoveryCancellation;
        if (coordinator is null || cancellation is null || cancellation.IsCancellationRequested)
            return;

        const string key = "graphics-device";
        _engineRecoveryCoordinator.TryStart(
            key,
            () => RunGraphicsDeviceRecoveryAsync(coordinator, key, detail, cancellation.Token),
            completed =>
            {
                if (completed.IsFaulted)
                {
                    MediaForgeDiagnostics.Report(
                        _externalDiagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.graphics_device_recovery_failed",
                        "Automatic graphics device recovery failed.",
                        nameof(MediaForgeEngine),
                        completed.Exception?.GetBaseException());
                }
            });
    }

    private void ScheduleEncodedOutputRecovery(RenderOutputId outputId, string detail)
    {
        var coordinator = _faultRecoveryCoordinator;
        var cancellation = _recoveryCancellation;
        if (coordinator is null || cancellation is null || cancellation.IsCancellationRequested)
            return;

        var key = $"encoded-output:{outputId}";
        _engineRecoveryCoordinator.TryStart(
            key,
            () => RunEncodedOutputRecoveryAsync(coordinator, outputId, key, detail, cancellation.Token),
            completed =>
            {
                if (completed.IsFaulted)
                {
                    MediaForgeDiagnostics.Report(
                        _externalDiagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "engine.encoded_output_recovery_failed",
                        $"Automatic recovery failed for encoded output {outputId}.",
                        nameof(MediaForgeEngine),
                        completed.Exception?.GetBaseException(),
                        outputId: outputId.Value);
                }
            });
    }

    private async Task RunSourceRecoveryAsync(
        FaultRecoveryCoordinator coordinator,
        SourceId sourceId,
        string resourceKey,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.HandleFaultAsync(
                    FaultRecoveryScenario.SourceProviderFailed,
                    resourceKey,
                    detail,
                    async ct =>
                    {
                        var manager = _sourceRuntimeManager;
                        if (manager is null || !manager.TryGetRuntime(sourceId, out var runtime))
                            return false;

                        await runtime.StopAsync(ct).ConfigureAwait(false);
                        await runtime.StartAsync(ct).ConfigureAwait(false);
                        return runtime.State == WTK.MediaForge.Core.Sources.MediaSourceState.Running;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunGraphicsDeviceRecoveryAsync(
        FaultRecoveryCoordinator coordinator,
        string resourceKey,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await coordinator.HandleFaultAsync(
                    FaultRecoveryScenario.VulkanDeviceLost,
                    resourceKey,
                    detail,
                    TryRecreateRenderBackendAsync,
                    cancellationToken)
                .ConfigureAwait(false);

            if (state.Status != FaultRecoveryStatus.Exhausted)
                return;

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (State == MediaForgeEngineState.Running)
                    SetState(MediaForgeEngineState.Failed);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunEncodedOutputRecoveryAsync(
        FaultRecoveryCoordinator coordinator,
        RenderOutputId outputId,
        string resourceKey,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.HandleFaultAsync(
                    FaultRecoveryScenario.EncoderUnavailable,
                    resourceKey,
                    detail,
                    ct => TryRecreateEncodedOutputRouteAsync(outputId, ct),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> TryRecreateEncodedOutputRouteAsync(
        RenderOutputId outputId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != MediaForgeEngineState.Running ||
                _currentProject is null ||
                _mediaPipelineRuntime is null ||
                _encodedOutputRouteFactory is null)
            {
                return false;
            }

            var output = _currentProject.Outputs.FirstOrDefault(candidate => candidate.Id == outputId);
            if (output is null || !output.Enabled || !_encodedOutputRouteFactory.CanCreate(output.TypeId))
                return false;

            var surfaceOutputId = _encodedOutputRouteFactory.ResolveSurfaceOutputId(_currentProject, output);
            var groupedOutputs = _currentProject.Outputs
                .Where(static candidate => candidate.Enabled)
                .Where(candidate => _encodedOutputRouteFactory.CanCreate(candidate.TypeId))
                .Where(candidate =>
                    _encodedOutputRouteFactory.ResolveSurfaceOutputId(_currentProject, candidate) == surfaceOutputId)
                .ToArray();

            if (groupedOutputs.Any(static candidate => candidate.TypeId == RenderOutputTypes.RecordingMp4))
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encoded_output_recovery_requires_recording_segment",
                    "Automatic encoder restart was not attempted because the route contains an MP4 recording; replacing the encoder in-place would overwrite or corrupt the active file.",
                    nameof(MediaForgeEngine),
                    outputId: outputId.Value);
                return false;
            }

            _mediaPipelineRuntime.TryGetSurfaceOutputId(outputId, out var previousSurfaceOutputId);

            await _encodedOutputRouteFactory
                .RecreateAsync(
                    _currentProject,
                    output,
                    _mediaPipelineRuntime,
                    StopTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            var surfaceOutput = _currentProject.Outputs.Single(candidate => candidate.Id == surfaceOutputId);
            var createdBinding = await EnsureAutomaticSurfaceBindingAsync(surfaceOutput, cancellationToken).ConfigureAwait(false);
            if (createdBinding && _outputSinks.TryGetValue(surfaceOutputId, out var entry))
            {
                await EnqueueBindOutputAsync(surfaceOutput, entry.Sink, entry.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!previousSurfaceOutputId.IsEmpty && previousSurfaceOutputId != surfaceOutputId)
                await RemoveSurfaceBindingAsync(previousSurfaceOutputId, CancellationToken.None).ConfigureAwait(false);
            _renderPump?.RequestFrame();
            return _mediaPipelineRuntime.IsEncodedOutputRegistered(outputId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryRecreateRenderBackendAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != MediaForgeEngineState.Running || _currentProject is null)
                return false;

            var oldPump = _renderPump;
            _renderPump = null;
            if (oldPump is not null)
                await oldPump.StopAsync(StopTimeout, cancellationToken).ConfigureAwait(false);

            var oldThread = _renderThread;
            if (oldThread is not null)
            {
                try
                {
                    oldThread.Dispose();
                }
                catch (Exception ex) when (!oldThread.IsRunning)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Warning,
                        "engine.graphics_device_old_thread_cleanup_failed",
                        "The failed render thread stopped with cleanup errors before device recreation.",
                        nameof(MediaForgeEngine),
                        ex);
                }

                if (oldThread.IsRunning)
                    throw new TimeoutException("Render thread remained alive during graphics device recovery.");
            }

            _renderThread = null;
            _renderThreadGuard = null;

            var oldBackend = _backend;
            if (oldBackend is not null)
            {
                oldBackend.Dispose();
                _backend = null;
            }

            var newGuard = new RenderThreadGuard();
            if (!_backendFactory.TryCreate(newGuard, _diagnostics, out var newBackend) || newBackend is null)
                return false;

            MediaForgeRenderThread? newThread = null;
            try
            {
                newThread = new MediaForgeRenderThread(
                    newBackend,
                    newGuard,
                    diagnostics: _diagnostics,
                    sinkDispatcher: _sinkDispatcher,
                    outputFrameConsumers: _mediaPipelineRuntime is null
                        ? Array.Empty<IRenderedOutputFrameConsumer>()
                        : [_mediaPipelineRuntime],
                    joinTimeout: RenderThreadJoinTimeout,
                    submissionShutdownTimeout: RenderThreadSubmissionShutdownTimeout);

                _renderThreadGuard = newGuard;
                _backend = newBackend;
                _renderThread = newThread;
                newThread.Start();

                foreach (var (outputId, entry) in _outputSinks)
                {
                    var output = _currentProject.Outputs.First(candidate => candidate.Id == outputId);
                    if (!output.Enabled)
                        continue;
                    await EnqueueBindOutputAsync(output, entry.Sink, entry.Target, cancellationToken)
                        .ConfigureAwait(false);
                }

                _renderPump = new MediaForgeRenderPump(
                    RenderFramesPerSecond,
                    CanPublishRenderFrame,
                    PublishScheduledRenderFrame,
                    GetScheduledTargetOutputs,
                    _diagnostics);
                _renderPump.RequestFrame();
                return true;
            }
            catch (Exception recoveryException)
            {
                var cleanupErrors = new List<Exception>();
                var candidateThreadStopped = true;

                if (newThread is not null)
                {
                    try
                    {
                        newThread.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupErrors.Add(cleanupException);
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "engine.graphics_device_candidate_thread_cleanup_failed",
                            "Failed to cleanup replacement render thread after recovery attempt.",
                            nameof(MediaForgeEngine),
                            cleanupException);
                    }

                    candidateThreadStopped = !newThread.IsRunning;
                }

                if (!candidateThreadStopped)
                {
                    _renderThreadGuard = newGuard;
                    _backend = newBackend;
                    _renderThread = newThread;

                    var ownershipFailure = new InvalidOperationException(
                        "Replacement render backend remains owned by a live render thread and cannot be destroyed safely.");
                    cleanupErrors.Add(ownershipFailure);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Fatal,
                        "engine.graphics_device_candidate_backend_retained",
                        ownershipFailure.Message,
                        nameof(MediaForgeEngine),
                        ownershipFailure);
                }
                else
                {
                    _renderThread = null;
                    _renderThreadGuard = null;
                    _backend = null;

                    try
                    {
                        newBackend.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupErrors.Add(cleanupException);
                        _backend = newBackend;
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "engine.graphics_device_candidate_backend_cleanup_failed",
                            "Failed to cleanup replacement render backend after recovery attempt.",
                            nameof(MediaForgeEngine),
                            cleanupException);
                    }
                }

                if (cleanupErrors.Count == 0)
                    throw;

                throw new AggregateException(
                    "Graphics device recovery failed and candidate resource cleanup was incomplete.",
                    [recoveryException, .. cleanupErrors]);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopActiveRecoveriesAsync(List<Exception> cleanupErrors)
    {
        var cancellation = _recoveryCancellation;
        _recoveryCancellation = null;
        if (cancellation is null)
            return;

        await cancellation.CancelAsync().ConfigureAwait(false);
        var recoveries = _engineRecoveryCoordinator.Snapshot();

        try
        {
            await Task.WhenAll(recoveries).WaitAsync(StopTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException ex)
        {
            cleanupErrors.Add(ex);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "engine.fault_recovery_shutdown_timeout",
                "Automatic recovery operations did not stop within the engine shutdown timeout.",
                nameof(MediaForgeEngine),
                ex);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OnRecoveryStateChanged(object? sender, FaultRecoveryState state) =>
        SafeRaiseEvent(
            nameof(RecoveryStateChanged),
            () => RecoveryStateChanged?.Invoke(
                this,
                new MediaForgeRecoveryEventArgs(ToPublicRecoverySnapshot(state))));

    private static MediaForgeRecoverySnapshot ToPublicRecoverySnapshot(FaultRecoveryState state) =>
        new()
        {
            ResourceId = state.ResourceKey,
            Area = state.Scenario switch
            {
                FaultRecoveryScenario.VulkanDeviceLost or FaultRecoveryScenario.GpuSwitch =>
                    MediaForgeRecoveryArea.GraphicsDevice,
                FaultRecoveryScenario.DecoderUnavailable => MediaForgeRecoveryArea.Decoder,
                FaultRecoveryScenario.EncoderUnavailable => MediaForgeRecoveryArea.Encoder,
                FaultRecoveryScenario.Mp4FinalizeFailed => MediaForgeRecoveryArea.Recording,
                FaultRecoveryScenario.RtmpDisconnected => MediaForgeRecoveryArea.Streaming,
                FaultRecoveryScenario.RenderExportFailed => MediaForgeRecoveryArea.Output,
                _ => MediaForgeRecoveryArea.Source
            },
            Status = state.Status switch
            {
                FaultRecoveryStatus.Recovered => MediaForgeRecoveryStatus.Recovered,
                FaultRecoveryStatus.Exhausted => MediaForgeRecoveryStatus.Exhausted,
                FaultRecoveryStatus.Canceled => MediaForgeRecoveryStatus.Canceled,
                _ => MediaForgeRecoveryStatus.Recovering
            },
            Message = state.Detail,
            AttemptCount = state.AttemptCount,
            LastAttemptUtc = state.LastAttemptUtc,
            PausesRecording = state.RequiresRecordingPause,
            PausesStreaming = state.RequiresStreamingPause,
            IsolatesSource = state.RequiresSourceIsolation
        };

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

    private sealed record ActiveSceneEditSession(
        SceneEditSessionDescriptor Descriptor,
        MediaForgeProject? DraftProject,
        bool HasChanges);

    private sealed class ActiveOutputSceneTransition(
        Guid operationId,
        RenderOutputId outputId,
        CanvasId sourceCanvasId,
        CanvasId destinationCanvasId,
        SceneVersionBinding destinationBinding) : IDisposable
    {
        private CancellationTokenRegistration _cancellationRegistration;
        private int _disposed;

        public object SyncRoot { get; } = new();
        public Guid OperationId { get; } = operationId;
        public RenderOutputId OutputId { get; } = outputId;
        public CanvasId SourceCanvasId { get; } = sourceCanvasId;
        public CanvasId DestinationCanvasId { get; } = destinationCanvasId;
        public SceneVersionBinding DestinationBinding { get; } = destinationBinding;
        public Task PhaseTail { get; set; } = Task.CompletedTask;
        public Exception? TerminalFailure { get; set; }
        public CancellationTokenRegistration CancellationRegistration
        {
            set
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    value.Dispose();
                    return;
                }

                _cancellationRegistration = value;
                if (Volatile.Read(ref _disposed) != 0)
                    _cancellationRegistration.Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _cancellationRegistration.Dispose();
        }
    }

    private sealed record OutputSceneTransitionCancellation(
        OutputRouteTransitionRuntime Runtime,
        RenderOutputId OutputId,
        Guid OperationId);

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
