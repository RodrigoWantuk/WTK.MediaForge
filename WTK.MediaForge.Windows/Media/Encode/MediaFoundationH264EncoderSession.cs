using Vortice.Direct3D11;
using WTK.MediaForge.Core.Media.Audit;
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

/// <summary>
/// Product Media Foundation H.264 hardware encoder boundary.
/// </summary>
internal sealed class MediaFoundationHardwareH264EncoderSession : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly int _width;
    private readonly int _height;
    private readonly string _pixelFormat;
    private bool _disposed;

    public MediaFoundationHardwareH264EncoderSession(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Encoder width must be positive.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Encoder height must be positive.");

        if (string.IsNullOrWhiteSpace(pixelFormat))
            throw new ArgumentException("Encoder pixel format is required.", nameof(pixelFormat));

        _width = width;
        _height = height;
        _pixelFormat = pixelFormat;
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _device;
        _ = _width;
        _ = _height;
        _ = _pixelFormat;
        throw CreateUnavailableException();
    }

    public EncodedSurfaceResult? TryEncodeSurface(
        D3D11SharedTextureFrameHandle surface,
        long frameNumber,
        TimeSpan presentationTime,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = frameNumber;
        _ = presentationTime;
        throw CreateUnavailableException();
    }

    public static NotSupportedException CreateUnavailableException() =>
        new(
            "Real Media Foundation H.264 hardware encoder output is unavailable until GPU surface input, MFT selection, format conversion, and backend packet validation are implemented. The prototype canned-packet bridge is not a product encoder backend.");

    public void Dispose() => _disposed = true;
}
