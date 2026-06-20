using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class RenderOutputTypeDescriptor
{
    public required RenderOutputTypeId TypeId { get; init; }

    public required string DisplayName { get; init; }

    public required bool RequiresWindowHandle { get; init; }

    public required bool IsHeadless { get; init; }
}
