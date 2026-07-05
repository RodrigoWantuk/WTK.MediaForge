namespace WTK.MediaForge.Core.Media;

public sealed class EncodedVideoPacketLease : IDisposable
{
    private int _disposed;
    private readonly Action? _onRelease;

    private EncodedVideoPacketLease(EncodedVideoPacket packet, Action? onRelease)
    {
        Packet = packet;
        _onRelease = onRelease;
    }

    public EncodedVideoPacket Packet { get; }

    public static EncodedVideoPacketLease Create(EncodedVideoPacket packet, Action? onRelease = null) =>
        new(packet, onRelease);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _onRelease?.Invoke();
    }
}
