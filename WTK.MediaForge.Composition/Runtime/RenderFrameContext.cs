namespace WTK.MediaForge.Composition.Runtime;

public readonly record struct RenderFrameContext(
    long FrameNumber,
    TimeSpan PresentationTime,
    TimeSpan DeltaTime,
    double TargetFps,
    CancellationToken CancellationToken);
