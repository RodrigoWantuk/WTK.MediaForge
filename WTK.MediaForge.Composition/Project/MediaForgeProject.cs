using WTK.MediaForge.Audio;

namespace WTK.MediaForge.Composition.Project;

public sealed class MediaForgeProject
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string CreatedWithVersion { get; set; } = "1.0.0";

    public string SavedWithVersion { get; set; } = "1.0.0";

    public List<MediaForgeSourceDefinition> SourceDefinitions { get; set; } = [];

    public List<MediaForgeCanvas> Canvases { get; set; } = [];

    public List<MediaForgeRenderOutput> Outputs { get; set; } = [];

    /// <summary>Portable global audio graph. Physical adapters remain capability-gated.</summary>
    public AudioGraphDefinition Audio { get; set; } = new();
}
