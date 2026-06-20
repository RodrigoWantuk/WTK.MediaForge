using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class OffscreenOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.Offscreen;

    public int SchemaVersion { get; init; } = 1;
}
