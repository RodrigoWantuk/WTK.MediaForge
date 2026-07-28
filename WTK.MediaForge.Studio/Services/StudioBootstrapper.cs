using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Runtime;

namespace WTK.MediaForge.Studio.Services;

public static class StudioBootstrapper
{
    public static async ValueTask<StudioApplicationSession> CreateRuntimeSessionAsync(
        IMediaForgeRuntimeFactory runtimeFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        var runtime = await runtimeFactory.CreateAsync(new RuntimeCreationRequest(), cancellationToken).ConfigureAwait(false);
        var mapper = new StudioProjectEngineMapper();
        var projectService = new RuntimeStudioProjectService(mapper);
        var capabilityService = new RuntimeStudioCapabilityService(runtime);
        var initialDocument = RuntimeStudioProjectService.CreateEmptyDocument();

        if (runtime.Availability != MediaForgeRuntimeAvailability.Available || runtime.Engine is null)
        {
            var reason = runtime.UnavailableReason ?? "No MediaForge runtime adapter is available.";
            return CreateUnavailableSession(
                runtime,
                projectService,
                capabilityService,
                initialDocument,
                reason);
        }

        var engine = runtime.Engine;
        var engineService = new RuntimeStudioEngineService(engine);
        var sceneEditService = new StudioSceneEditRuntimeService(
            new StudioSceneEditBridge(new MediaForgeStudioSceneEditEngine(engine)), mapper);
        var outputService = new RuntimeStudioOutputService(engine, capabilityService);
        var services = new StudioServiceBundle(
            projectService,
            engineService,
            sceneEditService,
            outputService,
            capabilityService,
            new StudioDialogService(capabilityService),
            new StudioUndoRedoService(),
            new StudioShortcutService(),
            new StudioLayoutService(),
            new StudioDiagnosticsService(),
            new StudioSelectionService(),
            new StudioInspectorPageFactory(),
            new AvaloniaStudioUiTimer(),
            initialDocument);

        return new StudioApplicationSession(
            new StudioShellViewModel(services),
            capabilityService,
            engineService);
    }

    private static StudioApplicationSession CreateUnavailableSession(
        MediaForgeRuntime runtime,
        RuntimeStudioProjectService projectService,
        RuntimeStudioCapabilityService capabilityService,
        StudioDocument initialDocument,
        string reason)
    {
        var engineService = new UnavailableStudioEngineService(reason);
        var services = new StudioServiceBundle(
            projectService,
            engineService,
            new UnavailableStudioSceneEditRuntimeService(reason),
            new UnavailableStudioOutputService(reason),
            capabilityService,
            new StudioDialogService(capabilityService),
            new StudioUndoRedoService(),
            new StudioShortcutService(),
            new StudioLayoutService(),
            new StudioDiagnosticsService(),
            new StudioSelectionService(),
            new StudioInspectorPageFactory(),
            new AvaloniaStudioUiTimer(),
            initialDocument);

        return new StudioApplicationSession(
            new StudioShellViewModel(services),
            capabilityService,
            engineService,
            runtime,
            startEngine: false);
    }
}

public sealed class StudioApplicationSession(
    StudioShellViewModel shell,
    RuntimeStudioCapabilityService capabilityService,
    IStudioEngineService engineService,
    IAsyncDisposable? unavailableRuntime = null,
    bool startEngine = true) : IAsyncDisposable
{
    private readonly RuntimeStudioCapabilityService _capabilityService = capabilityService;
    private readonly IStudioEngineService _engineService = engineService;
    private readonly IAsyncDisposable? _unavailableRuntime = unavailableRuntime;
    private readonly bool _startEngine = startEngine;

    public StudioShellViewModel Shell { get; } = shell;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _capabilityService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await Shell.InitializeAsync(cancellationToken, _startEngine).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Shell.DisposeAsync().ConfigureAwait(false);
        if (_engineService is IAsyncDisposable engineDisposer)
            await engineDisposer.DisposeAsync().ConfigureAwait(false);
        if (_unavailableRuntime is not null)
            await _unavailableRuntime.DisposeAsync().ConfigureAwait(false);
    }
}
