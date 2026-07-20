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
        var outputService = new RuntimeStudioOutputService();
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

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _capabilityService.RefreshAsync(cancellationToken);

    public ValueTask DisposeAsync() => _engineService.DisposeAsync();
}
