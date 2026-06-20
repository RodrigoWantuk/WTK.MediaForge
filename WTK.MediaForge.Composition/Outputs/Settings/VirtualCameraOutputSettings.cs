using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class VirtualCameraOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.VirtualCamera;

    public int SchemaVersion { get; init; } = 1;

    public string DeviceName { get; init; } = string.Empty;
}
