using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Decode;

public sealed class MediaFoundationHardwareVideoDecoder : IHardwareVideoDecoder, IHardwareFileVideoDecoder
{
    private readonly HardwareDecoderInfo _info;
    private readonly D3D11GpuDevice _gpuDevice;
    private readonly WindowsDecodeGpuTextureFactory _decodeTextureFactory;
    private readonly GpuResourcePool _resourcePool;
    private readonly bool _allowPrototypeDecoding;
    private MediaFoundationFileHardwareVideoDecoderSession? _hardwareSession;
    private string? _sourcePath;
    private int _width;
    private int _height;
    private long _frameNumber;
    private bool _disposed;

    public MediaFoundationHardwareVideoDecoder()
        : this(allowPrototypeDecoding: false)
    {
    }

    internal MediaFoundationHardwareVideoDecoder(bool allowPrototypeDecoding)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation hardware decoder requires Windows.");

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        _gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter, requireVideoSupport: true);
        _decodeTextureFactory = new WindowsDecodeGpuTextureFactory(_gpuDevice.Device);
        _resourcePool = new GpuResourcePool(_decodeTextureFactory);
        _allowPrototypeDecoding = allowPrototypeDecoding;
        _info = new HardwareDecoderInfo
        {
            Name = "Media Foundation H.264 Hardware MFT",
            Codec = EncodedVideoCodec.H264,
            Backend = "MediaFoundation-D3D11VA",
            ProducesGpuSurface = true
        };
    }

    internal MediaFoundationHardwareVideoDecoder(
        D3D11GpuDevice gpuDevice,
        IGpuTextureFactory textureFactory,
        bool allowPrototypeDecoding = false)
    {
        _gpuDevice = gpuDevice ?? throw new ArgumentNullException(nameof(gpuDevice));
        ArgumentNullException.ThrowIfNull(textureFactory);
        _decodeTextureFactory = textureFactory as WindowsDecodeGpuTextureFactory ??
            new WindowsDecodeGpuTextureFactory(_gpuDevice.Device, fallbackFactory: textureFactory);
        _resourcePool = new GpuResourcePool(_decodeTextureFactory);
        _allowPrototypeDecoding = allowPrototypeDecoding;
        _info = new HardwareDecoderInfo
        {
            Name = "Media Foundation H.264 Hardware MFT",
            Codec = EncodedVideoCodec.H264,
            Backend = "MediaFoundation-D3D11VA",
            ProducesGpuSurface = true
        };
    }

    private ID3D11Device Device => _gpuDevice.Device;

    public HardwareDecoderInfo Info => _info;

    public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_allowPrototypeDecoding)
            return OpenProductSessionAsync(context, auditSink);

        if (!File.Exists(context.SourcePath))
            throw new FileNotFoundException("Video file was not found.", context.SourcePath);

        if (!PrototypeMediaFoundationDecodeBridge.TryOpen(context.SourcePath, Device, out var width, out var height))
            throw new InvalidOperationException("Media Foundation hardware decoder could not open the source.");

        _sourcePath = context.SourcePath;
        _width = width;
        _height = height;
        return ValueTask.CompletedTask;
    }

    public ValueTask<DecodedGpuFrame?> DecodeAsync(
        DecodeFrameContext context,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Packet.Data.IsEmpty)
        {
            throw new InvalidOperationException(
                "Packet decoder path requires a real encoded packet. File video decode must use DecodeNextFrameAsync.");
        }

        return DecodeNextFrameAsync(
            new FileDecodeFrameContext
            {
                FrameNumber = context.FrameNumber,
                PresentationTime = context.PresentationTime,
                CancellationToken = context.CancellationToken
            },
            auditSink);
    }

    public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
        FileDecodeFrameContext context,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hardwareSession is not null && !_allowPrototypeDecoding)
        {
            using var decoded = _hardwareSession.DecodeNextFrame(context, auditSink);
            if (decoded is null)
                return ValueTask.FromResult<DecodedGpuFrame?>(null);

            var decodedLease = AcquireDecodedTextureLease(decoded);
            return ValueTask.FromResult<DecodedGpuFrame?>(new DecodedGpuFrame(
                decodedLease,
                decoded.PresentationTime,
                decoded.Duration));
        }

        if (_sourcePath is null)
            return ValueTask.FromResult<DecodedGpuFrame?>(null);

        if (!PrototypeMediaFoundationDecodeBridge.TryReadGpuFrame(_sourcePath))
        {
            return ValueTask.FromResult<DecodedGpuFrame?>(null);
        }

        var lease = _resourcePool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = _width,
            Height = _height,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.ExternalImport,
            Recyclable = false
        });

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareDecodeSucceeded,
            Source = nameof(MediaFoundationHardwareVideoDecoder),
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype,
            Detail = "Prototype decode path produced a GPU texture placeholder; real MF/D3D11VA decode output validation is not implemented yet."
        });

        _frameNumber++;
        return ValueTask.FromResult<DecodedGpuFrame?>(new DecodedGpuFrame(
            lease,
            context.PresentationTime,
            TimeSpan.FromMilliseconds(33)));
    }

    public ValueTask FlushAsync(IMediaTransportAuditSink auditSink)
    {
        _ = auditSink;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _hardwareSession?.Dispose();
        _hardwareSession = null;
        _resourcePool.Dispose();
        _gpuDevice.Dispose();
        PrototypeMediaFoundationDecodeBridge.Reset();
        return ValueTask.CompletedTask;
    }

    private ValueTask OpenProductSessionAsync(
        HardwareDecodeOpenContext context,
        IMediaTransportAuditSink auditSink)
    {
        _hardwareSession ??= new MediaFoundationFileHardwareVideoDecoderSession(Device);
        _hardwareSession.Open(context, auditSink);
        return ValueTask.CompletedTask;
    }

    private GpuTextureLease AcquireDecodedTextureLease(DecodedD3D11VideoFrame decoded)
    {
        _decodeTextureFactory.EnqueueDecodedTexture(
            decoded.Texture,
            checked((uint)decoded.Width),
            checked((uint)decoded.Height),
            decoded.Format,
            decoded.SubresourceIndex);

        return _resourcePool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = decoded.Width,
            Height = decoded.Height,
            Format = decoded.Format.ToString(),
            Usage = GpuTextureUsage.ExternalImport,
            Recyclable = false
        });
    }
}

internal sealed class WindowsDecodeGpuTextureFactory : IGpuTextureFactory
{
    private readonly ID3D11Device _device;
    private readonly IGpuTextureFactory? _fallbackFactory;
    private readonly Queue<WindowsDecodeGpuPhysicalTexture> _decodedTextures = new();
    private readonly object _gate = new();

    public WindowsDecodeGpuTextureFactory(ID3D11Device device, IGpuTextureFactory? fallbackFactory = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _fallbackFactory = fallbackFactory;
    }

    public void EnqueueDecodedTexture(
        ID3D11Texture2D decodedTexture,
        uint width,
        uint height,
        Format format,
        int subresourceIndex)
    {
        ArgumentNullException.ThrowIfNull(decodedTexture);
        var handle = D3D11SharedTextureFactory.CreateSharedTexture(_device, width, height, format);
        var context = _device.ImmediateContext;
        context.CopySubresourceRegion(
            handle.Texture,
            0,
            0,
            0,
            0,
            decodedTexture,
            checked((uint)subresourceIndex));
        context.Flush();

        lock (_gate)
            _decodedTextures.Enqueue(new WindowsDecodeGpuPhysicalTexture(_device, handle));
    }

    public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
    {
        lock (_gate)
        {
            if (_decodedTextures.Count > 0)
                return _decodedTextures.Dequeue();
        }

        if (_fallbackFactory is not null)
            return _fallbackFactory.CreateTexture(descriptor);

        return new WindowsDecodeGpuPhysicalTexture(
            _device,
            D3D11SharedTextureFactory.CreateSharedTexture(
                _device,
                (uint)descriptor.Width,
                (uint)descriptor.Height));
    }
}

internal sealed class WindowsDecodeGpuPhysicalTexture : IGpuPhysicalResource, IGpuFrameHandleProvider
{
    private readonly D3D11SharedTextureFrameHandle _handle;
    private readonly TaskCompletionSource _fullyDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _finalized;

    public WindowsDecodeGpuPhysicalTexture(ID3D11Device device, D3D11SharedTextureFrameHandle handle)
    {
        _ = device;
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    public Task FullyDisposed => _fullyDisposed.Task;

    public IGpuFrameHandle FrameHandle => _handle;

    public bool TryFinalizePhysicalResources()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return _fullyDisposed.Task.IsCompleted;

        _handle.Dispose();
        _fullyDisposed.TrySetResult();
        return true;
    }
}

internal sealed class MediaFoundationFileHardwareVideoDecoderSession : IDisposable
{
    private readonly ID3D11Device _device;
    private MediaFoundationRuntimeLease? _mediaFoundationRuntimeLease;
    private IMFDXGIDeviceManager? _deviceManager;
    private IMFSourceReader? _sourceReader;
    private int _width;
    private int _height;
    private TimeSpan _frameDuration = TimeSpan.FromMilliseconds(33);
    private bool _disposed;

    public MediaFoundationFileHardwareVideoDecoderSession(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public void Open(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(context.SourcePath))
            throw new FileNotFoundException("Video file was not found.", context.SourcePath);

        DisposeReaderResources();
        _mediaFoundationRuntimeLease = MediaFoundationRuntime.Acquire();

        try
        {
            _deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
            _deviceManager.ResetDevice(_device).CheckError();

            using var attributes = MediaFactory.MFCreateAttributes(6);
            attributes.Set(SourceReaderAttributeKeys.D3DManager, _deviceManager).CheckError();
            attributes.Set(SourceReaderAttributeKeys.DisableDxva, false).CheckError();
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true).CheckError();
            attributes.Set(SourceReaderAttributeKeys.EnableTranscodeOnlyTransforms, true).CheckError();
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, false).CheckError();
            attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, false).CheckError();

            _sourceReader = MediaFactory.MFCreateSourceReaderFromURL(context.SourcePath, attributes);
            _sourceReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            _sourceReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using var outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
            _sourceReader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, outputType);

            using var currentType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            (_width, _height) = ReadFrameSize(currentType, context.Session);
            _frameDuration = ReadFrameDuration(currentType);

            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareDecodeStarted,
                Source = nameof(MediaFoundationFileHardwareVideoDecoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
                Detail = $"Media Foundation SourceReader opened {Path.GetFileName(context.SourcePath)} as NV12 with D3D11 manager ({_width}x{_height})."
            });
        }
        catch (Exception ex)
        {
            DisposeReaderResources();
            throw CreateUnavailableException(ex);
        }
    }

    public DecodedD3D11VideoFrame? DecodeNextFrame(
        FileDecodeFrameContext context,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (_sourceReader is null)
            throw new InvalidOperationException("Media Foundation hardware decoder session is not open.");

        var sample = _sourceReader.ReadSample(
            SourceReaderIndex.FirstVideoStream,
            SourceReaderControlFlag.None,
            out _,
            out var flags,
            out var timestamp);
        using (sample)
        {
            if ((flags & SourceReaderFlag.EndOfStream) != 0)
                return null;

            if ((flags & SourceReaderFlag.Error) != 0)
                throw new InvalidOperationException("Media Foundation SourceReader reported an error while decoding.");

            if (sample is null)
                return null;

            var texture = TryGetDecodedTexture(sample);
            if (texture is null)
            {
                auditSink.Record(new MediaTransportAuditEvent
                {
                    Kind = MediaTransportAuditEventKind.HardwareDecodeUnavailable,
                    Source = nameof(MediaFoundationFileHardwareVideoDecoderSession),
                    EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
                    Detail = $"Media Foundation returned a sample with {sample.BufferCount} buffer(s), but none exposed IMFDXGIBuffer or IMFDXGICrossAdapterBuffer. CPU decoded samples are prohibited for product decode."
                });

                throw CreateUnavailableException(
                    new InvalidOperationException(
                        $"Media Foundation returned a decoded sample with {sample.BufferCount} buffer(s), but none exposed IMFDXGIBuffer or IMFDXGICrossAdapterBuffer."));
            }
            var presentationTime = timestamp >= 0
                ? TimeSpan.FromTicks(timestamp)
                : context.PresentationTime;
            var duration = sample.SampleDuration > 0
                ? TimeSpan.FromTicks(sample.SampleDuration)
                : _frameDuration;

            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareDecodeSucceeded,
                Source = nameof(MediaFoundationFileHardwareVideoDecoderSession),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated,
                Detail = "Media Foundation D3D11VA produced an IMFDXGIBuffer-backed NV12 texture."
            });

            return new DecodedD3D11VideoFrame(
                texture.Texture,
                _width,
                _height,
                Format.NV12,
                texture.SubresourceIndex,
                presentationTime,
                duration);
        }
    }

    private DecodedTextureReference? TryGetDecodedTexture(IMFSample sample)
    {
        var bufferCount = sample.BufferCount;
        for (var index = 0; index < bufferCount; index++)
        {
            using var buffer = sample.GetBufferByIndex(index);
            using var dxgiBuffer = buffer.QueryInterfaceOrNull<IMFDXGIBuffer>();
            if (dxgiBuffer is not null)
            {
                var texturePointer = dxgiBuffer.GetResource(typeof(ID3D11Texture2D).GUID);
                if (texturePointer != IntPtr.Zero)
                    return new DecodedTextureReference(
                        new ID3D11Texture2D(texturePointer),
                        checked((int)dxgiBuffer.SubresourceIndex));
            }

            using var crossAdapterBuffer = buffer.QueryInterfaceOrNull<IMFDXGICrossAdapterBuffer>();
            if (crossAdapterBuffer is not null)
            {
                var texturePointer = crossAdapterBuffer.GetResourceForDevice(
                    _device,
                    typeof(ID3D11Texture2D).GUID);
                if (texturePointer != IntPtr.Zero)
                {
                    var subresourceIndex = checked((int)crossAdapterBuffer.GetSubresourceIndexForDevice(_device));
                    return new DecodedTextureReference(
                        new ID3D11Texture2D(texturePointer),
                        subresourceIndex);
                }
            }
        }

        return null;
    }

    private sealed record DecodedTextureReference(ID3D11Texture2D Texture, int SubresourceIndex);

    public static NotSupportedException CreateUnavailableException() =>
        CreateUnavailableException(null);

    public static NotSupportedException CreateUnavailableException(Exception? innerException)
    {
        var detail = innerException is null
            ? string.Empty
            : $" Detail: {innerException.GetType().Name}: {innerException.Message}";
        return new NotSupportedException(
            "Real Media Foundation D3D11VA file decode requires an IMFDXGIBuffer-backed GPU sample. System-memory decoded samples and placeholder texture bridges are not product decoder backends." + detail,
            innerException);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeReaderResources();
    }

    private void DisposeReaderResources()
    {
        _sourceReader?.Dispose();
        _sourceReader = null;
        _deviceManager?.Dispose();
        _deviceManager = null;
        _mediaFoundationRuntimeLease?.Dispose();
        _mediaFoundationRuntimeLease = null;
    }

    private static (int Width, int Height) ReadFrameSize(
        IMFMediaType mediaType,
        HardwareDecodeSession fallback)
    {
        ulong packed;
        try
        {
            packed = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        }
        catch
        {
            return (fallback.Width, fallback.Height);
        }

        var width = (int)(packed >> 32);
        var height = (int)(packed & 0xFFFFFFFF);
        if (width > 0 && height > 0)
            return (width, height);

        return (fallback.Width, fallback.Height);
    }

    private static TimeSpan ReadFrameDuration(IMFMediaType mediaType)
    {
        ulong packed;
        try
        {
            packed = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameRate);
        }
        catch
        {
            return TimeSpan.FromMilliseconds(33);
        }

        var numerator = (uint)(packed >> 32);
        var denominator = (uint)(packed & 0xFFFFFFFF);
        if (numerator == 0)
            return TimeSpan.FromMilliseconds(33);

        if (denominator == 0)
            denominator = 1;

        var ticks = checked((long)(TimeSpan.TicksPerSecond * (double)denominator / numerator));
        return TimeSpan.FromTicks(Math.Max(1, ticks));
    }
}

internal sealed class DecodedD3D11VideoFrame : IDisposable
{
    public DecodedD3D11VideoFrame(
        ID3D11Texture2D texture,
        int width,
        int height,
        Format format,
        int subresourceIndex,
        TimeSpan presentationTime,
        TimeSpan duration)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        Width = width;
        Height = height;
        Format = format;
        SubresourceIndex = subresourceIndex;
        PresentationTime = presentationTime;
        Duration = duration;
    }

    public ID3D11Texture2D Texture { get; private set; }

    public int Width { get; }

    public int Height { get; }

    public Format Format { get; }

    public int SubresourceIndex { get; }

    public TimeSpan PresentationTime { get; }

    public TimeSpan Duration { get; }

    public void Dispose()
    {
        Texture.Dispose();
        Texture = null!;
    }
}

internal static class PrototypeMediaFoundationDecodeBridge
{
    private static readonly object Gate = new();
    private static string? _openedPath;
    private static int _width;
    private static int _height;
    private static bool _mfStarted;

    public static bool TryOpen(string path, ID3D11Device device, out int width, out int height)
    {
        _ = device;
        lock (Gate)
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(path))
            {
                width = 0;
                height = 0;
                return false;
            }

            _openedPath = path;
            _width = 640;
            _height = 360;
            width = _width;
            height = _height;
            return TryStartupMediaFoundation();
        }
    }

    public static bool TryReadGpuFrame(string path)
    {
        lock (Gate)
            return _openedPath == path && _mfStarted;
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _openedPath = null;
            _width = 0;
            _height = 0;
            _mfStarted = false;
        }
    }

    private static bool TryStartupMediaFoundation()
    {
        _mfStarted = OperatingSystem.IsWindows();
        return _mfStarted;
    }
}
