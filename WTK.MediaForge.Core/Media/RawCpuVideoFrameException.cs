namespace WTK.MediaForge.Core.Media;

public sealed class RawCpuVideoFrameException
{
    public required RawCpuVideoFrameExceptionKind Kind { get; init; }

    public required string Reason { get; init; }

    public required string Platform { get; init; }

    public required string SourceOrSinkType { get; init; }

    public bool RequiresUserVisibleDiagnostic { get; init; }

    public bool ProductAllowed { get; init; }

    public string? ExpirationOrReplacementPlan { get; init; }
}
