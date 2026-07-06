using Vortice.Direct3D11;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal readonly struct EncodedSurfaceResult
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public bool IsKeyFrame { get; init; }
}

/// <summary>
/// Media Foundation hardware H.264 encoder session bound to a D3D11 device.
/// </summary>
internal sealed class MediaFoundationH264EncoderSession : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly int _width;
    private readonly int _height;
    private bool _initialized;
    private bool _disposed;

    public MediaFoundationH264EncoderSession(ID3D11Device device, int width, int height)
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
        _initialized = MediaFoundationH264MftBridge.TryEnsureHardwareEncoder(_width, _height);

        if (!_initialized)
            throw new InvalidOperationException("No Media Foundation hardware H.264 encoder is available.");
    }

    public EncodedSurfaceResult? TryEncodeSurface(D3D11SharedTextureFrameHandle surface, long frameNumber)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
            return null;

        var packet = MediaFoundationH264MftBridge.TryEncodeSurface(
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
        MediaFoundationH264MftBridge.Reset();
    }
}
