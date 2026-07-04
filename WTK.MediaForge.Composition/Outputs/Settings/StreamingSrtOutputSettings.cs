using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class StreamingSrtOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.StreamingSrt;

    public int SchemaVersion { get; init; } = 1;

    public string Url { get; init; } = string.Empty;
}
