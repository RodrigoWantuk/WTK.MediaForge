namespace WTK.MediaForge.Composition.Validation;

public sealed class MediaForgeProjectValidationException : Exception
{
    public MediaForgeProjectValidationException(ProjectValidationResult validationResult)
        : base(CreateMessage(validationResult))
    {
        ValidationResult = validationResult ?? throw new ArgumentNullException(nameof(validationResult));
    }

    public ProjectValidationResult ValidationResult { get; }

    private static string CreateMessage(ProjectValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var message = string.Join(
            Environment.NewLine,
            validationResult.Issues.Select(issue => $"[{issue.Code}] {issue.Message}"));

        return $"Project validation failed:{Environment.NewLine}{message}";
    }
}
