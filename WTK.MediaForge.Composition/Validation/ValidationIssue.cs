namespace WTK.MediaForge.Composition.Validation;

public enum ValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Fatal = 3
}

public sealed class ValidationIssue
{
    public required ValidationSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public static ValidationIssue Error(string code, string message) =>
        new() { Severity = ValidationSeverity.Error, Code = code, Message = message };

    public static ValidationIssue Fatal(string code, string message) =>
        new() { Severity = ValidationSeverity.Fatal, Code = code, Message = message };
}
