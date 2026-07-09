using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Mux;

public interface IMp4Muxer : IAsyncDisposable
{
    ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken = default);

    ValueTask FinalizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Packets-only MP4 muxer. CPU handles container metadata only; no raw video frames.
/// </summary>
public sealed class EncodedPacketMp4Muxer : IMp4Muxer
{
    private readonly string _outputPath;
    private readonly IMediaTransportAuditSink? _auditSink;
    private readonly List<EncodedVideoPacket> _packets = [];
    private bool _finalized;
    private bool _disposed;

    public EncodedPacketMp4Muxer(string outputPath, IMediaTransportAuditSink? auditSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _auditSink = auditSink;
    }

    public IReadOnlyList<EncodedVideoPacket> BufferedPackets => _packets;

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
            throw new InvalidOperationException("MP4 muxer is already finalized.");

        _packets.Add(packet);
        _auditSink?.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = nameof(EncodedPacketMp4Muxer),
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype,
            Detail = $"Buffered encoded packet for '{_outputPath}'."
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask FinalizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
            return ValueTask.CompletedTask;

        _finalized = true;
        IsoBmffMp4Writer.WriteMp4(_outputPath, _packets);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _packets.Clear();
        return ValueTask.CompletedTask;
    }
}
