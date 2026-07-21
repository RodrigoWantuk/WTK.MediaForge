using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Windows;

namespace WTK.MediaForge.Studio.Services;

public static class StudioBootstrapper
{
    public static StudioApplicationSession CreateRuntimeSession()
    {
        var mapper = new StudioProjectEngineMapper();
        var projectService = new RuntimeStudioProjectService(mapper);
        var capabilityService = new RuntimeStudioCapabilityService();
        var engine = MediaForgeWindows.CreateEngine();
        var engineService = new RuntimeStudioEngineService(engine);
        var sceneEditService = new StudioSceneEditRuntimeService(
            new StudioSceneEditBridge(new MediaForgeStudioSceneEditEngine(engine)),
            mapper);
        var outputService = new RuntimeStudioOutputService(engine, capabilityService);
        var initialDocument = RuntimeStudioProjectService.CreateEmptyDocument();
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
}

public sealed class StudioApplicationSession(
    StudioShellViewModel shell,
    RuntimeStudioCapabilityService capabilityService,
    RuntimeStudioEngineService engineService) : IAsyncDisposable
{
    private readonly RuntimeStudioCapabilityService _capabilityService = capabilityService;
    private readonly RuntimeStudioEngineService _engineService = engineService;

    public StudioShellViewModel Shell { get; } = shell;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _capabilityService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await Shell.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Shell.DisposeAsync().ConfigureAwait(false);
        await _engineService.DisposeAsync().ConfigureAwait(false);
    }
}
