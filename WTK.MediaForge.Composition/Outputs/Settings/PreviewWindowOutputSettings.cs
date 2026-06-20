using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class PreviewWindowOutputSettings : IRenderOutputSettings
{
    public RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;

    public int SchemaVersion { get; init; } = 1;

    public string Title { get; init; } = "Preview";

    public bool EnableVSync { get; init; } = true;
}
