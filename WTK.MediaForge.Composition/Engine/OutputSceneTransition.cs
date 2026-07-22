using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Engine;

public enum OutputSceneTransitionStatus
{
    Started,
    SwitchPointReached,
    Completed,
    Cancelled,
    Failed
}

public sealed record OutputSceneTransitionResult(
    Guid OperationId,
    RenderOutputId OutputId,
    CanvasId SourceCanvasId,
    CanvasId DestinationCanvasId,
    SceneVersionBinding DestinationBinding,
    OutputRouteTransition Transition);

public sealed class OutputSceneTransitionEventArgs : EventArgs
{
    public required Guid OperationId { get; init; }
    public required RenderOutputId OutputId { get; init; }
    public required CanvasId SourceCanvasId { get; init; }
    public required CanvasId DestinationCanvasId { get; init; }
    public required OutputSceneTransitionStatus Status { get; init; }
    public required float Progress { get; init; }
    public Exception? Failure { get; init; }
}
