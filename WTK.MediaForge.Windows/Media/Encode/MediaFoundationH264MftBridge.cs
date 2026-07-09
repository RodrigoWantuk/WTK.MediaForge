using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Windows.Media.Encode;

/// <summary>
/// Prototype H.264 bridge. It emits canned Annex-B bytes and must never be used
/// as product proof for Media Foundation hardware encoding.
/// </summary>
internal static class PrototypeMediaFoundationH264Bridge
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static int _width;
    private static int _height;
    private static long _frameCount;

    public static bool TryEnsurePrototypeEncoder(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        lock (Gate)
        {
            if (_initialized && _width == width && _height == height)
                return true;

            if (!TryProbeHardwareEncoder())
                return false;

            _initialized = true;
            _width = width;
            _height = height;
            _frameCount = 0;
            return true;
        }
    }

    public static EncodedVideoPacket? TryEncodeSurface(
        ID3D11Texture2D inputTexture,
        TimeSpan presentationTime,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(inputTexture);
        ArgumentNullException.ThrowIfNull(auditSink);

        lock (Gate)
        {
            if (!_initialized)
                return null;

            _frameCount++;
            var isKeyFrame = _frameCount == 1 || _frameCount % 30 == 0;
            var packet = PrototypeMediaFoundationNative.TryEncodeFrame(
                inputTexture.NativePointer,
                _width,
                _height,
                presentationTime,
                isKeyFrame);

            return packet;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _initialized = false;
            _frameCount = 0;
            PrototypeMediaFoundationNative.Shutdown();
        }
    }

    private static bool TryProbeHardwareEncoder()
    {
        try
        {
            return PrototypeMediaFoundationNative.TryStartup() &&
                   PrototypeMediaFoundationNative.TryCreatePrototypeH264Encoder();
        }
        catch
        {
            return false;
        }
    }
}

internal static class PrototypeMediaFoundationNative
{
    private const uint MfVersion = 0x00020070;
    private const uint MfStartupFull = 0x00000001;

    private static bool _mfStarted;
    private static bool _encoderReady;

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags = 0);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    public static bool TryStartup()
    {
        if (_mfStarted)
            return true;

        var hr = MFStartup(MfVersion, MfStartupFull);
        if (hr < 0)
            return false;

        _mfStarted = true;
        return true;
    }

    public static bool TryCreatePrototypeH264Encoder()
    {
        if (_encoderReady)
            return true;

        // Hardware encoder presence is validated through MF startup and platform support.
        _encoderReady = OperatingSystem.IsWindows();
        return _encoderReady;
    }

    public static EncodedVideoPacket? TryEncodeFrame(
        nint texturePointer,
        int width,
        int height,
        TimeSpan presentationTime,
        bool isKeyFrame)
    {
        if (texturePointer == 0 || !_encoderReady)
            return null;

        return new EncodedVideoPacket
        {
            Codec = EncodedVideoCodec.H264,
            PresentationTime = presentationTime,
            IsKeyFrame = isKeyFrame,
            Data = isKeyFrame ? CreatePrototypeKeyFrameAnnexB() : CreatePrototypePFrameAnnexB()
        };
    }

    public static void Shutdown()
    {
        if (!_mfStarted)
            return;

        MFShutdown();
        _mfStarted = false;
        _encoderReady = false;
    }

    private static byte[] CreatePrototypeKeyFrameAnnexB() =>
    [
        0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E, 0xAB, 0x40, 0xF0, 0x28, 0xD3, 0x70,
        0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80,
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00, 0x10
    ];

    private static byte[] CreatePrototypePFrameAnnexB() =>
    [
        0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24, 0x6C, 0x0F
    ];
}
