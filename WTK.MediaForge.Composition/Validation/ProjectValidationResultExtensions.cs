namespace WTK.MediaForge.Composition.Validation;

public static class ProjectValidationResultExtensions
{
    public static void ThrowIfInvalid(this ProjectValidationResult result)
    {
        if (result.IsValid)
            return;

        var message = string.Join(
            Environment.NewLine,
            result.Issues.Select(i => $"[{i.Code}] {i.Message}"));

        throw new InvalidOperationException($"Project validation failed:{Environment.NewLine}{message}");
    }
}
