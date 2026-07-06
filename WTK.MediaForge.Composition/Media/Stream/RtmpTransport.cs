namespace WTK.MediaForge.Composition.Media.Stream;

public sealed class RtmpTransport : IDisposable
{
    private readonly string _url;
    private readonly List<FlvPacket> _sentPackets = [];
    private bool _connected;
    private bool _disposed;

    public RtmpTransport(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _url = url;
    }

    public string Url => _url;

    public bool IsConnected => _connected;

    public IReadOnlyList<FlvPacket> SentPackets => _sentPackets;

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _connected = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(FlvPacket packet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connected)
            throw new InvalidOperationException("RTMP transport is not connected.");

        _sentPackets.Add(packet);
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connected = false;
        _sentPackets.Clear();
    }
}
