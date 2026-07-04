using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class EncodedFileOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.EncodedFile;

    public int SchemaVersion { get; init; } = 1;

    public string Path { get; init; } = string.Empty;

    public string Container { get; init; } = "mp4";

    public string VideoCodec { get; init; } = "h264";

    public string AudioCodec { get; init; } = "aac";
}
