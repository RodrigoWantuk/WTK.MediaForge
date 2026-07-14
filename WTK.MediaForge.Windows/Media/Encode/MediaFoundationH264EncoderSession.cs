using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using SharpGen.Runtime;
using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media;

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
    private readonly HardwareVideoEncoderSettings _settings;
    private readonly Queue<HardwareEncoderInputSurfaceRetention> _pendingInputSurfaces = new();
    private IMFDXGIDeviceManager? _deviceManager;
    private IMFTransform? _transform;
    private MediaFoundationRuntimeLease? _mediaFoundationRuntimeLease;
    private string? _transformName;
    private ReadOnlyMemory<byte> _codecConfiguration;
    private bool _disposed;
    private bool _initialized;

    public MediaFoundationHardwareH264EncoderSession(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
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
            _mediaFoundationRuntimeLease = MediaFoundationRuntime.Acquire();
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
        HardwareEncoderInputSurfaceRetention retainedSurface,
        long frameNumber,
        TimeSpan presentationTime,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(retainedSurface);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Initialize();

        if (_transform is null)
            throw CreateUnavailableException();

        var inputTexture = retainedSurface.BackendSurface switch
        {
            D3D11SharedTextureFrameHandle surface => surface.Texture,
            ID3D11Texture2D texture => texture,
            _ => null
        };

        if (inputTexture is null)
        {
            retainedSurface.Dispose();
            throw CreateUnavailableException(
                new InvalidOperationException("Encoder retained input is not a D3D11 texture surface."));
        }

        IMFSample? inputSample = null;
        var acceptedSurface = false;

        try
        {
            inputSample = CreateInputSample(inputTexture, presentationTime);
            _transform.ProcessInput(0, inputSample, 0);
            acceptedSurface = true;
            _pendingInputSurfaces.Enqueue(retainedSurface);

            auditSink.Record(new MediaTransportAuditEvent
            {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = nameof(MediaFoundationHardwareH264EncoderSession),
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
            Detail = $"Media Foundation hardware MFT accepted D3D11 surface input ({_transformName ?? "unknown MFT"}, {_settings.Width}x{_settings.Height}@{_settings.FramesPerSecond}, {_settings.PixelFormat})."
            });

            return TryReadOutputPacket(frameNumber, auditSink);
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            if (!acceptedSurface)
                retainedSurface.Dispose();

            DisposeTransformResources();
            throw CreateUnavailableException(ex);
        }
        finally
        {
            inputSample?.Dispose();
        }
    }

    private EncodedSurfaceResult? TryReadOutputPacket(
        long frameNumber,
        IMediaTransportAuditSink auditSink)
    {
        IMFSample? outputSample = null;
        IMFMediaBuffer? outputBuffer = null;

        try
        {
            var outputInfo = _transform!.GetOutputStreamInfo(0);
            var outputFlags = (OutputStreamInfoFlags)outputInfo.Flags;
            var transformProvidesSamples =
                outputFlags.HasFlag(OutputStreamInfoFlags.OutputStreamProvidesSamples) ||
                outputFlags.HasFlag(OutputStreamInfoFlags.OutputStreamCanProvideSamples);
            if (!transformProvidesSamples)
            {
                outputSample = MediaFactory.MFCreateSample();
                outputBuffer = MediaFactory.MFCreateMemoryBuffer(Math.Max(outputInfo.Size, 1_048_576));
                outputSample.AddBuffer(outputBuffer);
            }

            var outputData = new OutputDataBuffer
            {
                StreamID = 0,
                Sample = transformProvidesSamples ? null : outputSample
            };

            var result = _transform.ProcessOutput(
                ProcessOutputFlags.None,
                1,
                ref outputData,
                out _);
            if (result.Code == Vortice.MediaFoundation.ResultCode.TransformNeedMoreInput.Code)
                return null;

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

            var sample = outputData.Sample ?? outputSample;
            if (sample is null)
                return null;

            var packet = ReadEncodedPacket(sample);
            if (packet.IsEmpty)
                return null;

            ReleaseCompletedInputSurface();

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
                IsKeyFrame = IsKeyFrame(sample, frameNumber)
            };
        }
        finally
        {
            outputBuffer?.Dispose();
            outputSample?.Dispose();
        }
    }

    private IMFTransform CreateHardwareTransform()
    {
        var inputType = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = ToVideoSubtype(_settings.PixelFormat)
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
        UnlockAsyncTransformIfRequired(transform);
        transform.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)_deviceManager!.NativePointer);

        var outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
        outputType.Set(MediaTypeAttributeKeys.AvgBitrate, checked((uint)_settings.BitrateBitsPerSecond)).CheckError();
        outputType.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, checked((uint)_settings.KeyFrameIntervalFrames)).CheckError();
        MediaFactory.MFSetAttributeSize(outputType, MediaTypeAttributeKeys.FrameSize, (uint)_settings.Width, (uint)_settings.Height).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.FrameRate, (uint)_settings.FramesPerSecond, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetOutputType(0, outputType, 0);

        var inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        inputType.Set(MediaTypeAttributeKeys.Subtype, ToVideoSubtype(_settings.PixelFormat)).CheckError();
        MediaFactory.MFSetAttributeSize(inputType, MediaTypeAttributeKeys.FrameSize, (uint)_settings.Width, (uint)_settings.Height).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.FrameRate, (uint)_settings.FramesPerSecond, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetInputType(0, inputType, 0);

        transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
    }

    private static void UnlockAsyncTransformIfRequired(IMFTransform transform)
    {
        IMFAttributes? attributes = null;
        try
        {
            attributes = transform.Attributes;
            var isAsync = TryGetUInt32(attributes, TransformAttributeKeys.TransformAsync) != 0;
            if (!isAsync)
                return;

            attributes
                .Set(TransformAttributeKeys.TransformAsyncUnlock, 1)
                .CheckError();
        }
        finally
        {
            attributes?.Dispose();
        }
    }

    private static uint TryGetUInt32(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetUInt32(key);
        }
        catch (SharpGenException)
        {
            return 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private IMFSample CreateInputSample(ID3D11Texture2D texture, TimeSpan presentationTime)
    {
        var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(
            typeof(ID3D11Texture2D).GUID,
            texture,
            0,
            false);

        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = presentationTime.Ticks;
        sample.SampleDuration = FrameDuration.Ticks;
        buffer.Dispose();
        return sample;
    }

    private TimeSpan FrameDuration =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _settings.FramesPerSecond);

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

    public static NotSupportedException CreateUnavailableException(Exception? innerException = null)
    {
        var detail = innerException is null
            ? string.Empty
            : $" Detail: {innerException.GetType().Name}: {innerException.Message}";
        return new NotSupportedException(
            "Real Media Foundation H.264 hardware encoder output is unavailable on this machine or driver. Product encoding requires a hardware MFT that accepts GPU surface input and produces backend-validated packets; the prototype canned-packet bridge is not a product encoder backend." + detail,
            innerException);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeTransformResources();
    }

    private void DisposeTransformResources()
    {
        TryFlushAndEndStream();
        ReleaseAllPendingInputSurfaces();
        _transform?.Dispose();
        _transform = null;
        _deviceManager?.Dispose();
        _deviceManager = null;
        _codecConfiguration = ReadOnlyMemory<byte>.Empty;
        _initialized = false;
        _mediaFoundationRuntimeLease?.Dispose();
        _mediaFoundationRuntimeLease = null;
    }

    private void ReleaseCompletedInputSurface()
    {
        if (_pendingInputSurfaces.Count == 0)
            return;

        _pendingInputSurfaces.Dequeue().Dispose();
    }

    private void ReleaseAllPendingInputSurfaces()
    {
        while (_pendingInputSurfaces.Count > 0)
            _pendingInputSurfaces.Dequeue().Dispose();
    }

    private void TryFlushAndEndStream()
    {
        if (_transform is null)
            return;

        try
        {
            _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
        }
        catch (SharpGenException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            _transform.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
        }
        catch (SharpGenException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            _transform.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero);
        }
        catch (SharpGenException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
