namespace WTK.MediaForge.Composition.Validation;

public sealed class ProjectValidationResult
{
    public static ProjectValidationResult Empty { get; } = new([]);

    public ProjectValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsValid => !Issues.Any(i =>
        i.Severity is ValidationSeverity.Error or ValidationSeverity.Fatal);
}
