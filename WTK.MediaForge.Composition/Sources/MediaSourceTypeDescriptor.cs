using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Sources;

public sealed class MediaSourceTypeDescriptor
{
    public required MediaSourceTypeId TypeId { get; init; }

    public required string DisplayName { get; init; }

    public required MediaSourceCategory Category { get; init; }

    public required MediaTransportKind OutputTransport { get; init; }

    public required bool IsLive { get; init; }

    public required bool IsTimeline { get; init; }

    public required bool HasVideo { get; init; }

    public required bool HasAudio { get; init; }

    public required bool RequiresGpuInterop { get; init; }

    public required bool RequiresHardwareDecode { get; init; }

    public required bool AllowsRawCpuException { get; init; }

    public RawCpuVideoFrameExceptionKind? RawCpuExceptionKind { get; init; }

    public MediaForgeSupportStatus SupportStatus { get; init; } = MediaForgeSupportStatus.Planned;

    public string? UnavailableReason { get; init; }
}
