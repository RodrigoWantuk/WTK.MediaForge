using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Services;

public sealed record StudioDialogOptionDescriptor(
    string Id,
    string Title,
    string Description,
    StudioIconKind IconKind,
    string Badge,
    bool IsEnabled);

public sealed record StudioTransitionOptionDescriptor(
    string Id,
    string Name,
    int DurationMs);

public sealed class StudioDialogRequest
{
    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string PrimaryText { get; init; } = "Confirmar";

    public string SecondaryText { get; init; } = "Cancelar";

    public string TargetOutputId { get; init; } = string.Empty;

    public string SelectedSceneId { get; init; } = string.Empty;

    public string SelectedTransitionId { get; init; } = "transition-cut";

    public int TransitionDurationMs { get; init; } = 120;

    public bool RequiresLiveConfirmation { get; init; }

    public IReadOnlyList<StudioDialogOptionDescriptor> Options { get; init; } = [];

    public IReadOnlyList<StudioTransitionOptionDescriptor> TransitionOptions { get; init; } = [];
}

public interface IStudioDialogService
{
    StudioDialogRequest CreateAddSourceRequest(StudioDocument document, StudioScene? currentScene);

    StudioDialogRequest CreateAddSceneRequest();

    StudioDialogRequest CreateConfigureOutputRequest(StudioDocument document);

    StudioDialogRequest CreateRouteOutputRequest(StudioDocument document, string outputId, string? currentSceneId);
}

public sealed class StudioDialogService(IStudioCapabilityService capabilityService) : IStudioDialogService
{
    private readonly IStudioCapabilityService _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));

    public StudioDialogRequest CreateAddSourceRequest(StudioDocument document, StudioScene? currentScene)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new StudioDialogRequest
        {
            Title = "Adicionar fonte",
            Message = $"Escolha uma fonte para adicionar à cena {currentScene?.DisplayName ?? "atual"}.",
            Kind = "source-library",
            PrimaryText = "Fechar",
            Options = _capabilityService
                .GetSourceCapabilities()
                .Select(static capability => new StudioDialogOptionDescriptor(
                    capability.TypeId,
                    capability.DisplayName,
                    capability.DialogDescription,
                    capability.IconKind,
                    capability.Badge,
                    capability.IsSelectable))
                .ToArray()
        };
    }

    public StudioDialogRequest CreateAddSceneRequest() =>
        new()
        {
            Title = "Adicionar cena",
            Message = "Cria uma cena vazia pronta para receber fontes.",
            Kind = "scene",
            PrimaryText = "Criar cena"
        };

    public StudioDialogRequest CreateConfigureOutputRequest(StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new StudioDialogRequest
        {
            Title = "Configurar saídas",
            Message = "Escolha uma saída validada para revisar ou configurar.",
            Kind = "output-library",
            PrimaryText = "Fechar",
            Options = _capabilityService
                .GetOutputCapabilities()
                .Select(capability =>
                {
                    var existing = document.Outputs.FirstOrDefault(output => output.TypeId == capability.TypeId);
                    var description = existing is null
                        ? capability.DialogDescription
                        : $"{capability.DialogDescription}. Atual: {existing.DisplayName} -> {AssignedSceneName(document, existing)}";

                    return new StudioDialogOptionDescriptor(
                        capability.TypeId,
                        capability.DisplayName,
                        description,
                        capability.IconKind,
                        capability.Badge,
                        capability.IsSelectable && existing is not null);
                })
                .ToArray()
        };
    }

    public StudioDialogRequest CreateRouteOutputRequest(StudioDocument document, string outputId, string? currentSceneId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);

        var output = document.Outputs.First(item => item.Id == outputId);
        var isLive = output.IsLive || output.State == StudioOutputState.Live;
        var defaultTransition = document.Transitions.FirstOrDefault(item => item.Id == output.DefaultTransitionId);
        var selectedSceneId = string.IsNullOrWhiteSpace(currentSceneId)
            ? output.AssignedSceneId
            : currentSceneId;

        return new StudioDialogRequest
        {
            Title = isLive ? "Transicionar cena da saída" : "Alterar cena da saída",
            Message = $"Escolha a cena e a transição para {output.DisplayName}.",
            Kind = "route-output",
            PrimaryText = isLive ? "Transicionar" : "Alterar",
            TargetOutputId = outputId,
            SelectedSceneId = selectedSceneId,
            SelectedTransitionId = output.DefaultTransitionId,
            TransitionDurationMs = output.TransitionDurationMs > 0
                ? output.TransitionDurationMs
                : defaultTransition?.DurationMs ?? 120,
            RequiresLiveConfirmation = isLive,
            TransitionOptions = document.Transitions
                .Select(static transition => new StudioTransitionOptionDescriptor(
                    transition.Id,
                    transition.DisplayName,
                    transition.DurationMs))
                .ToArray(),
            Options = document.Scenes
                .Select(static scene => new StudioDialogOptionDescriptor(
                    scene.Id,
                    scene.DisplayName,
                    $"{scene.Canvas.Width:0}×{scene.Canvas.Height:0} • {scene.Canvas.FrameRate:0.##} fps",
                    StudioIconKind.Scene,
                    scene.IsProgram ? "Principal" : string.Empty,
                    true))
                .ToArray()
        };
    }

    private static string AssignedSceneName(StudioDocument document, StudioOutput output) =>
        document.Scenes.FirstOrDefault(scene => scene.Id == output.AssignedSceneId)?.DisplayName ?? "Sem cena";
}
