namespace WTK.MediaForge.Core.Media.Audit;

public enum MediaTransportAuditEventKind
{
    GpuSurfaceExportStarted,
    GpuSurfaceExportSucceeded,
    CpuReadbackAttempted,
    StagingBufferCreated,
    HardwareEncoderInputLeaseCreated,
    EncodedPacketProduced
}

public sealed class MediaTransportAuditEvent
{
    public required MediaTransportAuditEventKind Kind { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Detail { get; init; }
}

public interface IMediaTransportAuditSink
{
    void Record(MediaTransportAuditEvent auditEvent);
}

public sealed class CollectingMediaTransportAuditSink : IMediaTransportAuditSink
{
    private readonly List<MediaTransportAuditEvent> _events = [];

    public IReadOnlyList<MediaTransportAuditEvent> Events => _events;

    public void Record(MediaTransportAuditEvent auditEvent) => _events.Add(auditEvent);

    public void Clear() => _events.Clear();

    public bool Contains(MediaTransportAuditEventKind kind) =>
        _events.Any(e => e.Kind == kind);
}

public static class MediaTransportAuditRules
{
    public static bool IsProductPathValid(IReadOnlyList<MediaTransportAuditEvent> events) =>
        events.Any(e => e.Kind == MediaTransportAuditEventKind.GpuSurfaceExportSucceeded)
        && events.Any(e => e.Kind == MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated)
        && !events.Any(e => e.Kind is MediaTransportAuditEventKind.CpuReadbackAttempted
            or MediaTransportAuditEventKind.StagingBufferCreated);
}
