using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class RecordingMp4OutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.RecordingMp4;

    public int SchemaVersion { get; init; } = 1;

    public string Path { get; init; } = string.Empty;
}
