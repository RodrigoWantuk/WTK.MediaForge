using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public interface IRenderOutputSettings
{
    RenderOutputTypeId TypeId { get; }

    int SchemaVersion { get; }
}
