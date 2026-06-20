using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Validation;

namespace WTK.MediaForge.Composition.Project;

public static class MediaForgeProjectMigrator
{
    public static ProjectMigrateResult Migrate(MediaForgeProject project)
    {
        if (project.SchemaVersion > MediaForgeProject.CurrentSchemaVersion)
        {
            return ProjectMigrateResult.Failed(new ProjectValidationResult([
                ValidationIssue.Error("schema.unsupported", $"Schema version {project.SchemaVersion} is newer than supported {MediaForgeProject.CurrentSchemaVersion}.")
            ]));
        }

        MigrateSourceDefinitions(project);
        return ProjectMigrateResult.Succeeded(project);
    }

    private static void MigrateSourceDefinitions(MediaForgeProject project)
    {
        foreach (var source in project.SourceDefinitions)
        {
            if (!MediaSourceTypeRegistry.IsLegacy(source.TypeId))
                continue;

            source.TypeId = MediaSourceTypeRegistry.ResolveCanonical(source.TypeId);
        }
    }
}

public sealed class ProjectMigrateResult
{
    public bool Success { get; init; }

    public MediaForgeProject? Project { get; init; }

    public ProjectValidationResult Validation { get; init; } = ProjectValidationResult.Empty;

    public static ProjectMigrateResult Succeeded(MediaForgeProject project) =>
        new() { Success = true, Project = project };

    public static ProjectMigrateResult Failed(ProjectValidationResult validation) =>
        new() { Success = false, Validation = validation };
}
