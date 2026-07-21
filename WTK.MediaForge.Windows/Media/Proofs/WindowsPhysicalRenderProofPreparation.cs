using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal static class WindowsPhysicalRenderProofPreparation
{
    public static void Execute(
        RenderFrameSnapshot snapshot,
        params RenderOutputId[] targetOutputs)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (targetOutputs.Length == 0)
            throw new ArgumentException("At least one target output is required.", nameof(targetOutputs));

        var plan = MediaForgeRenderGraphCompiler.Compile(snapshot);
        snapshot.RenderGraphExecution = RenderGraphExecutor.Execute(
            plan,
            new RenderGraphContext
            {
                FrameContext = new FrameExecutionContext
                {
                    FrameId = snapshot.Context.FrameNumber,
                    PresentationTime = snapshot.Context.PresentationTime,
                    FrameBudget = snapshot.Context.DeltaTime,
                    TargetOutputs = targetOutputs
                },
                SourceFrames = CreateSourceFrameMap(snapshot)
            });

        if (snapshot.RenderGraphExecution.PhysicalPlan.Operations.Count == 0)
        {
            throw new InvalidOperationException(
                "Media product proof compiled an empty physical RenderGraph plan.");
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
}
