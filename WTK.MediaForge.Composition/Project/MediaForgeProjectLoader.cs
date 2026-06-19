using System.Text.Json;
using WTK.MediaForge.Composition.Validation;

namespace WTK.MediaForge.Composition.Project;

public static class MediaForgeProjectLoader
{
    public static ProjectLoadResult LoadFromJson(string json)
    {
        try
        {
            var project = MediaForgeProjectSerializer.Deserialize(json);
            return Load(project);
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is NotSupportedException ||
            ex is InvalidOperationException ||
            ex is ArgumentException)
        {
            return new ProjectLoadResult
            {
                Project = null,
                Validation = new ProjectValidationResult([
                    ValidationIssue.Fatal(
                        "project.json.invalid",
                        $"Project JSON could not be loaded: {ex.Message}")
                ])
            };
        }
    }

    public static ProjectLoadResult Load(MediaForgeProject project)
    {
        var migrateResult = MediaForgeProjectMigrator.Migrate(project);
        if (!migrateResult.Success)
        {
            return new ProjectLoadResult
            {
                Project = null,
                Validation = migrateResult.Validation
            };
        }

        var validation = MediaForgeProjectValidator.Validate(migrateResult.Project!);
        return new ProjectLoadResult
        {
            Project = validation.IsValid ? migrateResult.Project : null,
            Validation = validation
        };
    }
}

public sealed class ProjectLoadResult
{
    public MediaForgeProject? Project { get; init; }

    public ProjectValidationResult Validation { get; init; } = ProjectValidationResult.Empty;
}
