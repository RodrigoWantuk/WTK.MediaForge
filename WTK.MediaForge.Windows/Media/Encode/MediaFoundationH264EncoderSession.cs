using Vortice.Direct3D11;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal readonly struct EncodedSurfaceResult
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public bool IsKeyFrame { get; init; }
}

/// <summary>
/// Prototype H.264 encoder session bound to a D3D11 device.
/// It emits canned packets and is only available through explicit internal test opt-in.
/// </summary>
internal sealed class PrototypeMediaFoundationH264EncoderSession : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly int _width;
    private readonly int _height;
    private bool _initialized;
    private bool _disposed;

    public PrototypeMediaFoundationH264EncoderSession(ID3D11Device device, int width, int height)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        _width = width;
        _height = height;
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _initialized = PrototypeMediaFoundationH264Bridge.TryEnsurePrototypeEncoder(_width, _height);

        if (!_initialized)
            throw new InvalidOperationException("Prototype H.264 encoder is unavailable.");
    }

    public EncodedSurfaceResult? TryEncodeSurface(D3D11SharedTextureFrameHandle surface, long frameNumber)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
            return null;

        var packet = PrototypeMediaFoundationH264Bridge.TryEncodeSurface(
            surface.Texture,
            TimeSpan.FromMilliseconds(frameNumber * 33),
            new Core.Media.Audit.CollectingMediaTransportAuditSink());

        if (packet is null)
            return null;

        return new EncodedSurfaceResult
        {
            Data = packet.Data,
            IsKeyFrame = packet.IsKeyFrame
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PrototypeMediaFoundationH264Bridge.Reset();
    }
}
