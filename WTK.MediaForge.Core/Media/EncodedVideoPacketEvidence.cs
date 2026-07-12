using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Core.Media;

public sealed class EncodedVideoPacketEvidence
{
    private EncodedVideoPacketEvidence(
        MediaTransportAuditEvidenceKind kind,
        string source,
        string? backend,
        string? proofId)
    {
        Kind = kind;
        Source = source;
        Backend = backend;
        ProofId = proofId;
    }

    public static EncodedVideoPacketEvidence ContractOnly { get; } =
        new(MediaTransportAuditEvidenceKind.ContractOnly, "Contract", null, null);

    public MediaTransportAuditEvidenceKind Kind { get; }

    public string Source { get; }

    public string? Backend { get; }

    public string? ProofId { get; }

    internal static EncodedVideoPacketEvidence CreatePrototype(string source, string? backend = null) =>
        new(MediaTransportAuditEvidenceKind.Prototype, RequireSource(source), backend, null);

    internal static EncodedVideoPacketEvidence CreateBackendOutputValidated(
        string source,
        string backend,
        string proofId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(proofId);

        return new(
            MediaTransportAuditEvidenceKind.BackendOutputValidated,
            RequireSource(source),
            backend,
            proofId);
    }

    private static string RequireSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return source;
    }
}
