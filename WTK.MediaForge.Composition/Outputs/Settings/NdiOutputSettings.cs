using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class NdiOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.Ndi;

    public int SchemaVersion { get; init; } = 1;

    public string SourceName { get; init; } = string.Empty;
}
