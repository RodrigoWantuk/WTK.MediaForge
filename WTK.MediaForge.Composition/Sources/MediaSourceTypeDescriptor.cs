using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources;

public sealed class MediaSourceTypeDescriptor
{
    public required MediaSourceTypeId TypeId { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsLive { get; init; }

    public required bool HasVideo { get; init; }

    public required bool HasAudio { get; init; }

    public required bool RequiresGpuInterop { get; init; }
}
