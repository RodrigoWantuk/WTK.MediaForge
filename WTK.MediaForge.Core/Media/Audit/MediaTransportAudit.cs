namespace WTK.MediaForge.Core.Media.Audit;

public enum MediaTransportAuditEventKind
{
    GpuSurfaceExportStarted,
    GpuSurfaceExportSucceeded,
    CpuReadbackAttempted,
    StagingBufferCreated,
    GpuFormatConversionStarted,
    GpuFormatConversionSucceeded,
    GpuFormatConversionUnavailable,
    HardwareEncoderInputLeaseCreated,
    HardwareEncoderAcceptedSurface,
    HardwareDecodeSucceeded,
    DecodedFrameAdaptedToSourceFrame,
    SourceFrameSubmittedToRenderer,
    EncodedPacketProduced
}

public enum MediaTransportAuditEvidenceKind
{
    ContractOnly,
    Prototype,
    BackendCallSucceeded,
    BackendOutputValidated
}

public sealed class MediaTransportAuditEvent
{
    public required MediaTransportAuditEventKind Kind { get; init; }

    public required string Source { get; init; }

    public MediaTransportAuditEvidenceKind EvidenceKind { get; init; } =
        MediaTransportAuditEvidenceKind.ContractOnly;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Detail { get; init; }
}

public interface IMediaTransportAuditSink
{
    void Record(MediaTransportAuditEvent auditEvent);
}

public sealed class CollectingMediaTransportAuditSink : IMediaTransportAuditSink
{
    private readonly object _gate = new();
    private readonly List<MediaTransportAuditEvent> _events = [];

    public IReadOnlyList<MediaTransportAuditEvent> Events
    {
        get
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    public void Record(MediaTransportAuditEvent auditEvent)
    {
        lock (_gate)
            _events.Add(auditEvent);
    }

    public void Clear()
    {
        lock (_gate)
            _events.Clear();
    }

    public bool Contains(MediaTransportAuditEventKind kind) =>
        Events.Any(e => e.Kind == kind);
}

public static class MediaTransportAuditRules
{
    public static bool IsProductPathValid(IReadOnlyList<MediaTransportAuditEvent> events) =>
        HasEvidence(events, MediaTransportAuditEventKind.GpuSurfaceExportSucceeded, MediaTransportAuditEvidenceKind.BackendCallSucceeded)
        && HasEvidence(events, MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated, MediaTransportAuditEvidenceKind.BackendCallSucceeded)
        && !events.Any(e => e.Kind is MediaTransportAuditEventKind.CpuReadbackAttempted
            or MediaTransportAuditEventKind.StagingBufferCreated);

    public static bool IsExportProofPathValid(IReadOnlyList<MediaTransportAuditEvent> events) =>
        IsProductPathValid(events)
        && HasEvidence(events, MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface, MediaTransportAuditEvidenceKind.BackendOutputValidated)
        && HasEvidence(events, MediaTransportAuditEventKind.EncodedPacketProduced, MediaTransportAuditEvidenceKind.BackendOutputValidated);

    public static bool IsDecodePathValid(IReadOnlyList<MediaTransportAuditEvent> events) =>
        HasEvidence(events, MediaTransportAuditEventKind.HardwareDecodeSucceeded, MediaTransportAuditEvidenceKind.BackendOutputValidated)
        && !events.Any(e => e.Kind is MediaTransportAuditEventKind.CpuReadbackAttempted
            or MediaTransportAuditEventKind.StagingBufferCreated);

    public static bool IsDecodeToRenderProofPathValid(IReadOnlyList<MediaTransportAuditEvent> events) =>
        IsDecodePathValid(events)
        && HasEvidence(events, MediaTransportAuditEventKind.DecodedFrameAdaptedToSourceFrame, MediaTransportAuditEvidenceKind.BackendOutputValidated)
        && HasEvidence(events, MediaTransportAuditEventKind.SourceFrameSubmittedToRenderer, MediaTransportAuditEvidenceKind.BackendOutputValidated);

    private static bool HasEvidence(
        IReadOnlyList<MediaTransportAuditEvent> events,
        MediaTransportAuditEventKind kind,
        MediaTransportAuditEvidenceKind minimumEvidence) =>
        events.Any(e => e.Kind == kind && e.EvidenceKind >= minimumEvidence);
}
