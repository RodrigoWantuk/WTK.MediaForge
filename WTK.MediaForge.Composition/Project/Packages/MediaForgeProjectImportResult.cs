using WTK.MediaForge.Composition.Validation;

namespace WTK.MediaForge.Composition.Project.Packages;

public sealed class MediaForgeProjectImportResult
{
    public MediaForgeProject? Project { get; init; }

    public ProjectValidationResult Validation { get; init; } = ProjectValidationResult.Empty;

    public bool Applied { get; init; }
}
