using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class StreamingRtmpOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.StreamingRtmp;

    public int SchemaVersion { get; init; } = 1;

    public string Url { get; init; } = string.Empty;

    public string StreamKey { get; init; } = string.Empty;
}
