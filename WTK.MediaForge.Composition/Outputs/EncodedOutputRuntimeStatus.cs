using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public enum EncodedOutputRuntimeStatus
{
    Stopped,
    Starting,
    Running,
    Backpressure,
    Failed,
    Unavailable
}

public sealed record EncodedOutputRuntimeSnapshot(
    RenderOutputId OutputId,
    EncodedOutputRuntimeStatus Status,
    string? Reason,
    long FramesSubmitted,
    long PacketsProduced,
    long PacketsWritten,
    long FramesDropped,
    TimeSpan LastPacketLatency);
