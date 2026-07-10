using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Composition.Runtime.Scheduling;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderGraphContext
{
    public required FrameExecutionContext FrameContext { get; init; }

    public SceneRuntimeSnapshot? SceneSnapshot { get; init; }

    public IReadOnlyDictionary<SourceId, GpuFrameReference> SourceFrames { get; init; } =
        new Dictionary<SourceId, GpuFrameReference>();

    public Dictionary<string, RenderGraphNodeResult> NodeResults { get; } = new(StringComparer.Ordinal);
}

internal sealed class RenderGraphNodeResult
{
    public required string NodeKey { get; init; }

    public required RenderGraphNodeKind Kind { get; init; }

    public bool WasSkipped { get; init; }

    public string? FailureReason { get; init; }

    public GpuFrameReference? SourceFrame { get; init; }

    public GpuTextureLease? OutputTexture { get; init; }

    public bool HasRenderableResource => SourceFrame.HasValue || OutputTexture is not null;
}
