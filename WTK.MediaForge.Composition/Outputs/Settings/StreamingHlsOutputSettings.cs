using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class StreamingHlsOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.StreamingHls;

    public int SchemaVersion { get; init; } = 1;

    public string Path { get; init; } = string.Empty;
}
