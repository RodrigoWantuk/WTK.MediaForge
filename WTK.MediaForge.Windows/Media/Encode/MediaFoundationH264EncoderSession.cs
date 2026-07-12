using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using SharpGen.Runtime;
using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal readonly struct EncodedSurfaceResult
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public ReadOnlyMemory<byte> CodecConfiguration { get; init; }

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
            CodecConfiguration = packet.CodecConfiguration,
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
    private IMFDXGIDeviceManager? _deviceManager;
    private IMFTransform? _transform;
    private string? _transformName;
    private ReadOnlyMemory<byte> _codecConfiguration;
    private bool _disposed;
    private bool _initialized;
    private bool _mediaFoundationStarted;

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
        if (_initialized)
            return;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation hardware encoder requires Windows.");

        try
        {
            MediaFactory.MFStartup(true).CheckError();
            _mediaFoundationStarted = true;
            _deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
            _deviceManager.ResetDevice(_device).CheckError();
            _transform = CreateHardwareTransform();
            ConfigureTransform(_transform);
            _initialized = true;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            DisposeTransformResources();
            throw CreateUnavailableException(ex);
        }
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
        Initialize();

        if (_transform is null)
            throw CreateUnavailableException();

        IMFSample? inputSample = null;
        IMFSample? outputSample = null;
        IMFMediaBuffer? outputBuffer = null;

        try
        {
            inputSample = CreateInputSample(surface, presentationTime);
            _transform.ProcessInput(0, inputSample, 0);

            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
                Source = nameof(MediaFoundationHardwareH264EncoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
                Detail = $"Media Foundation hardware MFT accepted D3D11 surface input ({_transformName ?? "unknown MFT"})."
            });

            outputSample = MediaFactory.MFCreateSample();
            var outputInfo = _transform.GetOutputStreamInfo(0);
            outputBuffer = MediaFactory.MFCreateMemoryBuffer(Math.Max(outputInfo.Size, 1_048_576));
            outputSample.AddBuffer(outputBuffer);

            var outputData = new OutputDataBuffer
            {
                StreamID = 0,
                Sample = outputSample
            };

            var processStatus = ProcessOutputStatus.ProcessOutputStatusNewStreams;
            var result = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref outputData, out processStatus);
            if (result.Code == Vortice.MediaFoundation.ResultCode.TransformNeedMoreInput.Code)
            {
                return null;
            }

            if (result.Code == Vortice.MediaFoundation.ResultCode.TransformStreamChange.Code)
            {
                throw CreateUnavailableException(
                    new InvalidOperationException(
                        "Media Foundation hardware encoder requested an output stream change. Dynamic output type renegotiation is not accepted by the current product proof path."));
            }

            if (result.Failure)
            {
                throw CreateUnavailableException(
                    new InvalidOperationException(
                        $"Media Foundation hardware encoder did not produce an output packet. HRESULT={result.Code:X8} {result.Description}"));
            }

            var packet = ReadEncodedPacket(outputData.Sample ?? outputSample);
            if (packet.IsEmpty)
                return null;

            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
                Source = nameof(MediaFoundationHardwareH264EncoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated,
                Detail = $"Media Foundation hardware MFT produced a real H.264 packet ({packet.Length} bytes)."
            });

            return new EncodedSurfaceResult
            {
                Data = packet,
                CodecConfiguration = TryReadCodecConfiguration(),
                IsKeyFrame = IsKeyFrame(outputData.Sample ?? outputSample, frameNumber)
            };
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            throw CreateUnavailableException(ex);
        }
        finally
        {
            outputBuffer?.Dispose();
            outputSample?.Dispose();
            inputSample?.Dispose();
        }
    }

    private IMFTransform CreateHardwareTransform()
    {
        var inputType = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = ToVideoSubtype(_pixelFormat)
        };
        var outputType = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264
        };

        using var activations = MediaFactory.MFTEnumEx(
            TransformCategoryGuids.VideoEncoder,
            (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter),
            inputType,
            outputType);

        var activation = activations.FirstOrDefault()
            ?? throw new InvalidOperationException("No Media Foundation hardware H.264 encoder MFT accepted the requested GPU input/output type.");

        _transformName = TryGetTransformName(activation);
        return activation.ActivateObject<IMFTransform>();
    }

    private void ConfigureTransform(IMFTransform transform)
    {
        transform.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)_deviceManager!.NativePointer);

        var outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
        outputType.Set(MediaTypeAttributeKeys.AvgBitrate, 8_000_000u).CheckError();
        MediaFactory.MFSetAttributeSize(outputType, MediaTypeAttributeKeys.FrameSize, (uint)_width, (uint)_height).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.FrameRate, 60, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetOutputType(0, outputType, 0);

        var inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        inputType.Set(MediaTypeAttributeKeys.Subtype, ToVideoSubtype(_pixelFormat)).CheckError();
        MediaFactory.MFSetAttributeSize(inputType, MediaTypeAttributeKeys.FrameSize, (uint)_width, (uint)_height).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.FrameRate, 60, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetInputType(0, inputType, 0);

        transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
    }

    private static IMFSample CreateInputSample(D3D11SharedTextureFrameHandle surface, TimeSpan presentationTime)
    {
        var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(
            typeof(ID3D11Texture2D).GUID,
            surface.Texture,
            0,
            false);

        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = presentationTime.Ticks;
        sample.SampleDuration = TimeSpan.FromMilliseconds(33).Ticks;
        buffer.Dispose();
        return sample;
    }

    private static ReadOnlyMemory<byte> ReadEncodedPacket(IMFSample sample)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        var currentLength = buffer.CurrentLength;
        if (currentLength <= 0)
            return ReadOnlyMemory<byte>.Empty;

        nint dataPointer = 0;
        var maxLength = 0;
        var lockedLength = 0;
        buffer.Lock(out dataPointer, out maxLength, out lockedLength);
        try
        {
            var bytes = new byte[currentLength];
            Marshal.Copy(dataPointer, bytes, 0, currentLength);
            return bytes;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private ReadOnlyMemory<byte> TryReadCodecConfiguration()
    {
        if (!_codecConfiguration.IsEmpty)
            return _codecConfiguration;

        if (_transform is null)
            return ReadOnlyMemory<byte>.Empty;

        try
        {
            using var outputType = _transform.GetOutputCurrentType(0);
            var blob = outputType.GetBlob(MediaTypeAttributeKeys.MpegSequenceHeader);
            if (blob.Length == 0)
                return ReadOnlyMemory<byte>.Empty;

            _codecConfiguration = blob;
            return _codecConfiguration;
        }
        catch (SharpGenException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        catch (InvalidOperationException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private static bool IsKeyFrame(IMFSample sample, long frameNumber)
    {
        try
        {
            return sample.GetUInt32(SampleAttributeKeys.CleanPoint) != 0;
        }
        catch (SharpGenException)
        {
            return frameNumber == 1;
        }
        catch (InvalidOperationException)
        {
            return frameNumber == 1;
        }
    }

    private static Guid ToVideoSubtype(string pixelFormat)
    {
        if (pixelFormat.Equals("NV12", StringComparison.OrdinalIgnoreCase))
            return VideoFormatGuids.NV12;

        if (pixelFormat.Equals("B8G8R8A8_UNORM", StringComparison.OrdinalIgnoreCase) ||
            pixelFormat.Equals("R8G8B8A8_UNORM", StringComparison.OrdinalIgnoreCase))
            return VideoFormatGuids.Rgb32;

        throw new NotSupportedException($"Media Foundation hardware H.264 encoder does not support input pixel format '{pixelFormat}' yet.");
    }

    private static string? TryGetTransformName(IMFActivate activation)
    {
        try
        {
            return activation.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);
        }
        catch
        {
            return null;
        }
    }

    public static NotSupportedException CreateUnavailableException(Exception? innerException = null) =>
        new(
            "Real Media Foundation H.264 hardware encoder output is unavailable on this machine or driver. Product encoding requires a hardware MFT that accepts GPU surface input and produces backend-validated packets; the prototype canned-packet bridge is not a product encoder backend.",
            innerException);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeTransformResources();
    }

    private void DisposeTransformResources()
    {
        _transform?.Dispose();
        _transform = null;
        _deviceManager?.Dispose();
        _deviceManager = null;
        _codecConfiguration = ReadOnlyMemory<byte>.Empty;
        _initialized = false;

        if (_mediaFoundationStarted)
        {
            MediaFactory.MFShutdown().CheckError();
            _mediaFoundationStarted = false;
        }
    }
}
