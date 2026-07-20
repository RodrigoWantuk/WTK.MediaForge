using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

/// <summary>
/// Media Foundation hardware encoder. GPU surface in, H.264 packets out.
/// </summary>
public sealed class MediaFoundationHardwareVideoEncoder : IHardwareVideoEncoder
{
    private readonly HardwareEncoderInfo _info;
    private readonly HardwareEncoderInputRequirement _inputRequirement;
    private readonly HardwareVideoEncoderSettings _settings;
    private readonly ID3D11Device _device;
    private readonly OwnedD3D11EncoderDevice? _ownedDevice;
    private readonly IHardwareEncoderFormatConverter? _formatConverter;
    private MediaFoundationHardwareH264EncoderSession? _hardwareSession;
    private long _frameNumber;
    private bool _disposed;

    public MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat = "B8G8R8A8_UNORM")
        : this(device, CreateSettings(width, height, pixelFormat), formatConverter: new D3D11BgraToNv12Converter(device))
    {
    }

    public MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings)
        : this(device, settings, formatConverter: new D3D11BgraToNv12Converter(device))
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat,
        IHardwareEncoderFormatConverter? formatConverter)
        : this(device, CreateSettings(width, height, pixelFormat), formatConverter)
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings,
        IHardwareEncoderFormatConverter? formatConverter)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
        _formatConverter = formatConverter;
        _info = new HardwareEncoderInfo
        {
            Name = "Media Foundation H.264 Hardware MFT",
            Codec = EncodedVideoCodec.H264,
            Backend = "MediaFoundation-HardwareMft",
            AcceptsGpuSurfaceInput = true
        };

        _inputRequirement = new HardwareEncoderInputRequirement
        {
            Width = _settings.Width,
            Height = _settings.Height,
            PixelFormat = _settings.PixelFormat,
            FramesPerSecond = _settings.FramesPerSecond,
            RequiresGpuSurface = true
        };
    }

    public MediaFoundationHardwareVideoEncoder(
        int width,
        int height,
        string pixelFormat = "NV12")
        : this(CreateOwnedDefaultDevice(), CreateSettings(width, height, pixelFormat))
    {
    }

    private MediaFoundationHardwareVideoEncoder(
        OwnedD3D11EncoderDevice ownedDevice,
        HardwareVideoEncoderSettings settings)
        : this(ownedDevice.Device, settings)
    {
        _ownedDevice = ownedDevice;
    }

    public HardwareEncoderInfo Info => _info;

    public HardwareEncoderInputRequirement InputRequirement => _inputRequirement;

    public ValueTask<EncodedVideoPacket?> EncodeAsync(
        EncodeFrameContext context,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (context.InputLease.BackendSurface is not (D3D11SharedTextureFrameHandle or ID3D11Texture2D))
        {
            throw MediaFoundationHardwareH264EncoderSession.CreateUnavailableException(
                new InvalidOperationException("Encoder requires a D3D11 texture backend surface."));
        }

        HardwareEncoderInputSurfaceRetention? retainedSurface =
            context.InputLease.RetainBackendSurfaceForAsyncConsumer();
        EncodedSurfaceResult? result;
        try
        {
            result = EnsureProductBackendAvailable()
                .TryEncodeSurface(
                    retainedSurface,
                    Interlocked.Increment(ref _frameNumber),
                    context.PresentationTime,
                    auditSink);
            retainedSurface = null;
        }
        finally
        {
            retainedSurface?.Dispose();
        }

        if (result is null)
            return ValueTask.FromResult<EncodedVideoPacket?>(null);

        return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
        {
            Data = result.Value.Data,
            Codec = EncodedVideoCodec.H264,
            BitstreamFormat = H264NalUtilities.ContainsValidStartCode(result.Value.Data.Span)
                ? EncodedVideoBitstreamFormat.AnnexB
                : EncodedVideoBitstreamFormat.Avcc,
            PresentationTime = result.Value.PresentationTime,
            Duration = result.Value.Duration,
            IsKeyFrame = result.Value.IsKeyFrame,
            CodecConfiguration = result.Value.CodecConfiguration,
            Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
                nameof(MediaFoundationHardwareVideoEncoder),
                _info.Backend,
                MediaForgeCapabilityCatalog.HardwareEncodeProof)
        });
    }

    public async ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
        GpuTextureLease textureLease,
        HardwareEncodeFrameContext context,
        IGpuFrameExporter frameExporter,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(textureLease);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frameExporter);
        ArgumentNullException.ThrowIfNull(auditSink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureProductBackendAvailable();

        context.CancellationToken.ThrowIfCancellationRequested();

        var descriptor = textureLease.ToGpuVideoFrameDescriptor();
        using var inputLease = await CreateEncoderInputLeaseAsync(
                textureLease,
                descriptor,
                frameExporter,
                auditSink,
                context.CancellationToken)
            .ConfigureAwait(false);

        var encodeContext = new EncodeFrameContext
        {
            InputLease = inputLease,
            FrameNumber = context.FrameId,
            PresentationTime = context.PresentationTime,
            CancellationToken = context.CancellationToken
        };

        return await EncodeAsync(encodeContext, auditSink).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hardwareSession is null)
            return ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>(Array.Empty<EncodedVideoPacket>());

        var drained = _hardwareSession.Drain(Interlocked.Read(ref _frameNumber), auditSink);
        var packets = drained.Select(ToEncodedVideoPacket).ToArray();
        return ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>(packets);
    }

    private async ValueTask<HardwareEncoderInputLease> CreateEncoderInputLeaseAsync(
        GpuTextureLease textureLease,
        GpuVideoFrameDescriptor descriptor,
        IGpuFrameExporter frameExporter,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        if (frameExporter.CanExport(descriptor, _inputRequirement))
        {
            return await frameExporter
                .ExportForEncoderAsync(descriptor, auditSink, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_formatConverter is not null &&
            _formatConverter.CanConvert(descriptor, _inputRequirement))
        {
            return await _formatConverter
                .ConvertAsync(textureLease, _inputRequirement, auditSink, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "GPU frame exporter cannot export the provided texture lease and no compatible GPU format converter is available.");
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        List<Exception>? errors = null;
        try
        {
            _hardwareSession?.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
        finally
        {
            _hardwareSession = null;
        }

        try
        {
            _ownedDevice?.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (errors is not null)
            throw new AggregateException("Failed to finalize Media Foundation hardware encoder resources.", errors);

        return ValueTask.CompletedTask;
    }

    private EncodedVideoPacket ToEncodedVideoPacket(EncodedSurfaceResult result) => new()
    {
        Data = result.Data,
        Codec = EncodedVideoCodec.H264,
        BitstreamFormat = H264NalUtilities.ContainsValidStartCode(result.Data.Span)
            ? EncodedVideoBitstreamFormat.AnnexB
            : EncodedVideoBitstreamFormat.Avcc,
        PresentationTime = result.PresentationTime,
        Duration = result.Duration > TimeSpan.Zero ? result.Duration : FrameDuration,
        IsKeyFrame = result.IsKeyFrame,
        CodecConfiguration = result.CodecConfiguration,
        Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
            nameof(MediaFoundationHardwareVideoEncoder),
            _info.Backend,
            MediaForgeCapabilityCatalog.HardwareEncodeProof)
    };

    private MediaFoundationHardwareH264EncoderSession EnsureProductBackendAvailable()
    {
        _hardwareSession ??= new MediaFoundationHardwareH264EncoderSession(
            _device,
            _settings);
        _hardwareSession.Initialize();
        return _hardwareSession;
    }

    private TimeSpan FrameDuration =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _settings.FramesPerSecond);

    private static HardwareVideoEncoderSettings CreateSettings(
        int width,
        int height,
        string pixelFormat) =>
        new()
        {
            Width = width,
            Height = height,
            PixelFormat = pixelFormat
        };

    private static OwnedD3D11EncoderDevice CreateOwnedDefaultDevice()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation hardware encoder requires Windows.");

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        return OwnedD3D11EncoderDevice.Create(adapter);
    }
}
