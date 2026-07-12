using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Composition.Media.Mux;

internal interface IMp4Muxer : IAsyncDisposable
{
    ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken = default);

    ValueTask FinalizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Prototype-only packet collector for the experimental ISO BMFF writer.
/// CPU handles container metadata only; no raw video frames.
/// This buffers all packets in memory and must not be treated as product MP4 recording.
/// </summary>
internal sealed class PrototypeEncodedPacketMp4Muxer : IMp4Muxer
{
    private readonly string _outputPath;
    private readonly IMediaTransportAuditSink? _auditSink;
    private readonly List<EncodedVideoPacket> _packets = [];
    private bool _finalized;
    private bool _disposed;

    public PrototypeEncodedPacketMp4Muxer(string outputPath, IMediaTransportAuditSink? auditSink = null)
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
            Source = nameof(PrototypeEncodedPacketMp4Muxer),
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
