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

    public TimeSpan PresentationTime { get; init; }

    public TimeSpan Duration { get; init; }
}

internal enum MediaFoundationH264EncoderSessionState
{
    Created,
    Streaming,
    Draining,
    Drained,
    Failed,
    Disposed
}

/// <summary>
/// Product Media Foundation H.264 hardware encoder boundary.
/// </summary>
internal sealed class MediaFoundationHardwareH264EncoderSession : IDisposable
{
    private const int MaxOutputDrainIterations = 64;

    private readonly ID3D11Device _device;
    private readonly HardwareVideoEncoderSettings _settings;
    private readonly List<PendingInputSurface> _pendingInputSurfaces = [];
    private readonly Queue<EncodedSurfaceResult> _pendingOutputPackets = new();
    private IMFDXGIDeviceManager? _deviceManager;
    private IMFTransform? _transform;
    private MediaFoundationRuntimeLease? _mediaFoundationRuntimeLease;
    private string? _transformName;
    private ReadOnlyMemory<byte> _codecConfiguration;
    private MediaFoundationH264EncoderSessionState _state = MediaFoundationH264EncoderSessionState.Created;
    private bool _acceptedInput;
    private H264Profile? _negotiatedProfile;
    private H264Level? _negotiatedLevel;

    public MediaFoundationHardwareH264EncoderSession(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
    }

    internal MediaFoundationH264EncoderSessionState StateForTests => _state;

    internal H264Profile? NegotiatedProfile => _negotiatedProfile;

    internal H264Level? NegotiatedLevel => _negotiatedLevel;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_state == MediaFoundationH264EncoderSessionState.Disposed, this);
        if (_state == MediaFoundationH264EncoderSessionState.Streaming)
            return;
        if (_state != MediaFoundationH264EncoderSessionState.Created)
            throw new InvalidOperationException($"Cannot initialize a Media Foundation encoder session in state '{_state}'.");

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation hardware encoder requires Windows.");

        try
        {
            _mediaFoundationRuntimeLease = MediaFoundationRuntime.Acquire();
            _deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
            _deviceManager.ResetDevice(_device).CheckError();
            _transform = CreateConfiguredHardwareTransform();
            _state = MediaFoundationH264EncoderSessionState.Streaming;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            _state = MediaFoundationH264EncoderSessionState.Failed;
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
        ObjectDisposedException.ThrowIf(_state == MediaFoundationH264EncoderSessionState.Disposed, this);
        Initialize();

        if (_state != MediaFoundationH264EncoderSessionState.Streaming)
            throw new InvalidOperationException($"Cannot submit encoder input in state '{_state}'.");

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
            Interlocked.Exchange(ref _lastSubmittedFrameNumber, frameNumber);
            _acceptedInput = true;
            acceptedSurface = true;
            _pendingInputSurfaces.Add(new PendingInputSurface(presentationTime, retainedSurface));
            EnforcePendingInputSurfaceLimit();

            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
                Source = nameof(MediaFoundationHardwareH264EncoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
                Detail = $"Media Foundation hardware MFT accepted D3D11 surface input ({_transformName ?? "unknown MFT"}, {_settings.Width}x{_settings.Height}@{_settings.FramesPerSecond}, {_settings.PixelFormat})."
            });

            DrainAvailableOutputPackets(frameNumber, auditSink);
            return _pendingOutputPackets.Count > 0
                ? _pendingOutputPackets.Dequeue()
                : null;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            if (!acceptedSurface)
                retainedSurface.Dispose();

            _state = MediaFoundationH264EncoderSessionState.Failed;
            DisposeTransformResources();
            throw CreateUnavailableException(ex);
        }
        finally
        {
            inputSample?.Dispose();
        }
    }

    private void DrainAvailableOutputPackets(
        long frameNumber,
        IMediaTransportAuditSink auditSink)
    {
        var drainIterations = 0;
        while (true)
        {
            if (++drainIterations > MaxOutputDrainIterations)
            {
                throw CreateUnavailableException(
                    new InvalidOperationException(
                        $"Media Foundation hardware encoder exceeded {MaxOutputDrainIterations} output drain iteration(s) for one input surface. The backend returned too many empty or partial outputs to be safe for sustained product encoding."));
            }

            IMFSample? outputSample = null;
            IMFMediaBuffer? outputBuffer = null;
            OutputDataBuffer outputData = default;
            var outputInfo = _transform!.GetOutputStreamInfo(0);
            var outputFlags = (OutputStreamInfoFlags)outputInfo.Flags;
            var transformProvidesSamples =
                outputFlags.HasFlag(OutputStreamInfoFlags.OutputStreamProvidesSamples) ||
                outputFlags.HasFlag(OutputStreamInfoFlags.OutputStreamCanProvideSamples);

            try
            {
                if (!transformProvidesSamples)
                {
                    outputSample = MediaFactory.MFCreateSample();
                    outputBuffer = MediaFactory.MFCreateMemoryBuffer(Math.Max(outputInfo.Size, 1_048_576));
                    outputSample.AddBuffer(outputBuffer);
                }

                outputData = new OutputDataBuffer
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
                    return;

                if (result.Code == Vortice.MediaFoundation.ResultCode.TransformStreamChange.Code)
                {
                    throw CreateUnavailableException(
                        new InvalidOperationException(
                            "Media Foundation hardware encoder requested an output stream change. Dynamic output type renegotiation is not accepted by the current product proof path."));
                }

                if (result.Failure)
                {
                    if (IsOutputNotReadyQuirk(result))
                        return;

                    throw CreateUnavailableException(
                        new InvalidOperationException(
                            $"Media Foundation hardware encoder did not produce an output packet. HRESULT={result.Code:X8} {result.Description}"));
                }

                var sample = outputData.Sample ?? outputSample;
                if (sample is null)
                    continue;

                var packet = ReadEncodedPacket(sample);
                if (packet.IsEmpty)
                    continue;

                var packetPresentationTime = TryGetSampleTime(sample, frameNumber);
                var packetDuration = TryGetSampleDuration(sample);
                ReleaseCompletedInputSurface(packetPresentationTime);

                auditSink.Record(new MediaTransportAuditEvent
                {
                    Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
                    Source = nameof(MediaFoundationHardwareH264EncoderSession),
                    EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated,
                    Detail = $"Media Foundation hardware MFT produced a real H.264 packet ({packet.Length} bytes)."
                });

                _pendingOutputPackets.Enqueue(new EncodedSurfaceResult
                {
                    Data = packet,
                    CodecConfiguration = TryReadCodecConfiguration(),
                    IsKeyFrame = IsKeyFrame(sample, frameNumber),
                    PresentationTime = packetPresentationTime,
                    Duration = packetDuration
                });
            }
            finally
            {
                if (outputData.Sample is not null && !ReferenceEquals(outputData.Sample, outputSample))
                    outputData.Sample.Dispose();

                outputBuffer?.Dispose();
                outputSample?.Dispose();
            }
        }
    }

    private static bool IsOutputNotReadyQuirk(Result result) =>
        result.Code == unchecked((int)0x8000FFFF);

    private IMFTransform CreateConfiguredHardwareTransform()
    {
        var outputType = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264
        };

        using var activations = MediaFactory.MFTEnumEx(
            TransformCategoryGuids.VideoEncoder,
            (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter),
            null,
            outputType);

        var failures = new List<string>();
        foreach (var activation in activations)
        {
            var transformName = TryGetTransformName(activation);
            IMFTransform? candidate = null;
            try
            {
                candidate = activation.ActivateObject<IMFTransform>();
                ConfigureTransform(candidate);
                _transformName = transformName;
                return candidate;
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                failures.Add($"{transformName}: {ex.GetType().Name} 0x{ex.HResult:X8} - {ex.Message}");
                candidate?.Dispose();
            }
        }

        var detail = failures.Count == 0
            ? "No hardware H.264 encoder MFT was enumerated."
            : $"No enumerated hardware H.264 encoder accepted {_settings.PixelFormat} GPU input. Candidates: {string.Join(" | ", failures)}";
        throw new InvalidOperationException(detail);
    }

    private void ConfigureTransform(IMFTransform transform)
    {
        UnlockAsyncTransformIfRequired(transform);
        transform.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)_deviceManager!.NativePointer);

        using var outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
        outputType.Set(MediaTypeAttributeKeys.AvgBitrate, checked((uint)_settings.BitrateBitsPerSecond)).CheckError();
        outputType.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, checked((uint)_settings.KeyFrameIntervalFrames)).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Mpeg2Profile, (uint)_settings.H264Profile).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Mpeg2Level, (uint)_settings.H264Level).CheckError();
        MediaFactory.MFSetAttributeSize(outputType, MediaTypeAttributeKeys.FrameSize, (uint)_settings.Width, (uint)_settings.Height).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.FrameRate, (uint)_settings.FramesPerSecond, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetOutputType(0, outputType, 0);

        using var inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        inputType.Set(MediaTypeAttributeKeys.Subtype, ToVideoSubtype(_settings.PixelFormat)).CheckError();
        MediaFactory.MFSetAttributeSize(inputType, MediaTypeAttributeKeys.FrameSize, (uint)_settings.Width, (uint)_settings.Height).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.FrameRate, (uint)_settings.FramesPerSecond, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        transform.SetInputType(0, inputType, 0);

        using var negotiatedOutputType = transform.GetOutputCurrentType(0);
        var negotiatedProfile = negotiatedOutputType.GetUInt32(MediaTypeAttributeKeys.Mpeg2Profile);
        var negotiatedLevel = negotiatedOutputType.GetUInt32(MediaTypeAttributeKeys.Mpeg2Level);
        if (negotiatedProfile != (uint)_settings.H264Profile ||
            negotiatedLevel != (uint)_settings.H264Level)
        {
            throw new InvalidOperationException(
                $"Media Foundation hardware MFT negotiated H.264 profile/level {negotiatedProfile}/{negotiatedLevel}, " +
                $"but {_settings.H264Profile}/{_settings.H264Level} was required.");
        }

        _negotiatedProfile = (H264Profile)negotiatedProfile;
        _negotiatedLevel = (H264Level)negotiatedLevel;

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

    private TimeSpan TryGetSampleTime(IMFSample sample, long frameNumber)
    {
        try
        {
            return TimeSpan.FromTicks(sample.SampleTime);
        }
        catch (Exception ex) when (ex is SharpGenException or InvalidOperationException)
        {
            return TimeSpan.FromTicks(checked((frameNumber - 1) * FrameDuration.Ticks));
        }
    }

    private TimeSpan TryGetSampleDuration(IMFSample sample)
    {
        try
        {
            var duration = TimeSpan.FromTicks(sample.SampleDuration);
            return duration > TimeSpan.Zero ? duration : FrameDuration;
        }
        catch (Exception ex) when (ex is SharpGenException or InvalidOperationException)
        {
            return FrameDuration;
        }
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

    public static NotSupportedException CreateUnavailableException(Exception? innerException = null)
    {
        var detail = innerException is null
            ? string.Empty
            : $" Detail: {innerException.GetType().Name}: {innerException.Message}";
        return new NotSupportedException(
            "Media Foundation H.264 hardware encoder output is unavailable on this machine or driver. Product encoding requires a hardware MFT that accepts GPU surface input and produces backend-validated packets." + detail,
            innerException);
    }

    public void Dispose()
    {
        if (_state == MediaFoundationH264EncoderSessionState.Disposed)
            return;

        var abandonedAcceptedInput =
            _state == MediaFoundationH264EncoderSessionState.Streaming && _acceptedInput;
        _state = MediaFoundationH264EncoderSessionState.Disposed;
        DisposeTransformResources();

        if (abandonedAcceptedInput)
        {
            throw new InvalidOperationException(
                "Media Foundation hardware encoder was disposed after accepting input but before DrainAsync completed. " +
                "Delayed packets cannot be discarded during successful route finalization.");
        }
    }

    private void DisposeTransformResources()
    {
        ReleaseAllPendingInputSurfaces();
        _transform?.Dispose();
        _transform = null;
        _deviceManager?.Dispose();
        _deviceManager = null;
        _codecConfiguration = ReadOnlyMemory<byte>.Empty;
        _negotiatedProfile = null;
        _negotiatedLevel = null;
        _pendingOutputPackets.Clear();
        _mediaFoundationRuntimeLease?.Dispose();
        _mediaFoundationRuntimeLease = null;
    }

    private void ReleaseCompletedInputSurface(TimeSpan presentationTime)
    {
        if (_pendingInputSurfaces.Count == 0)
            return;

        var index = _pendingInputSurfaces.FindIndex(item => item.PresentationTime == presentationTime);
        if (index < 0)
            index = 0;

        var pending = _pendingInputSurfaces[index];
        _pendingInputSurfaces.RemoveAt(index);
        pending.Retention.Dispose();
    }

    private void EnforcePendingInputSurfaceLimit()
    {
        if (_pendingInputSurfaces.Count <= _settings.MaxPendingInputSurfaces)
            return;

        ReleaseAllPendingInputSurfaces();
        DisposeTransformResources();
        throw CreateUnavailableException(
            new InvalidOperationException(
                $"Media Foundation hardware encoder retained more than {_settings.MaxPendingInputSurfaces} pending input surface(s) without output. This driver/backend is not safe for sustained product encoding."));
    }

    private void ReleaseAllPendingInputSurfaces()
    {
        while (_pendingInputSurfaces.Count > 0)
        {
            var pending = _pendingInputSurfaces[0];
            _pendingInputSurfaces.RemoveAt(0);
            pending.Retention.Dispose();
        }
    }

    private long _lastSubmittedFrameNumber;

    public IReadOnlyList<EncodedSurfaceResult> Drain(
        long lastFrameNumber,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(auditSink);

        ObjectDisposedException.ThrowIf(_state == MediaFoundationH264EncoderSessionState.Disposed, this);
        if (_state == MediaFoundationH264EncoderSessionState.Drained)
            return DrainPendingOutputPackets();

        if (_state == MediaFoundationH264EncoderSessionState.Created)
        {
            _state = MediaFoundationH264EncoderSessionState.Drained;
            return DrainPendingOutputPackets();
        }

        if (_state != MediaFoundationH264EncoderSessionState.Streaming || _transform is null)
            throw new InvalidOperationException($"Cannot drain a Media Foundation encoder session in state '{_state}'.");

        _state = MediaFoundationH264EncoderSessionState.Draining;

        try
        {
            SendEndOfStream();
            SendDrainCommand();
            DrainAvailableOutputPackets(Math.Max(lastFrameNumber, 1), auditSink);

            if (_acceptedInput && _codecConfiguration.IsEmpty)
            {
                throw new InvalidOperationException(
                    "Media Foundation encoder completed drain without H.264 SPS/PPS codec configuration.");
            }

            if (_pendingInputSurfaces.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Media Foundation encoder completed drain with {_pendingInputSurfaces.Count} retained input surface(s)." );
            }

            _transform.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero);
            _state = MediaFoundationH264EncoderSessionState.Drained;
            _acceptedInput = false;
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareEncoderDrainCompleted,
                Source = nameof(MediaFoundationHardwareH264EncoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
                Detail = "Media Foundation hardware encoder completed end-of-stream, drain, and flush."
            });
            return DrainPendingOutputPackets();
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            _state = MediaFoundationH264EncoderSessionState.Failed;
            DisposeTransformResources();
            throw new InvalidOperationException(
                "Media Foundation encoder finalization failed; delayed packets and the current output route are invalid.",
                ex);
        }
    }

    private void SendEndOfStream()
    {
        try
        {
            _transform!.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is SharpGenException or InvalidOperationException)
        {
            throw new InvalidOperationException("Media Foundation encoder rejected end-of-stream notification.", ex);
        }
    }

    private void SendDrainCommand()
    {
        try
        {
            _transform!.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is SharpGenException or InvalidOperationException)
        {
            throw new InvalidOperationException("Media Foundation encoder rejected drain command.", ex);
        }
    }

    private IReadOnlyList<EncodedSurfaceResult> DrainPendingOutputPackets()
    {
        if (_pendingOutputPackets.Count == 0)
            return Array.Empty<EncodedSurfaceResult>();

        var packets = _pendingOutputPackets.ToArray();
        _pendingOutputPackets.Clear();
        return packets;
    }

    private sealed record PendingInputSurface(
        TimeSpan PresentationTime,
        HardwareEncoderInputSurfaceRetention Retention);

}
