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
    private readonly bool _allowPrototypeEncoding;
    private readonly IHardwareEncoderFormatConverter? _formatConverter;
    private PrototypeMediaFoundationH264EncoderSession? _prototypeSession;
    private MediaFoundationHardwareH264EncoderSession? _hardwareSession;
    private long _frameNumber;
    private bool _disposed;

    public MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat = "B8G8R8A8_UNORM")
        : this(device, CreateSettings(width, height, pixelFormat), allowPrototypeEncoding: false)
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat,
        bool allowPrototypeEncoding)
        : this(device, CreateSettings(width, height, pixelFormat), allowPrototypeEncoding)
    {
    }

    public MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings)
        : this(device, settings, allowPrototypeEncoding: false)
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings,
        bool allowPrototypeEncoding)
        : this(device, settings, allowPrototypeEncoding, formatConverter: new D3D11BgraToNv12Converter(device))
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat,
        bool allowPrototypeEncoding,
        IHardwareEncoderFormatConverter? formatConverter)
        : this(device, CreateSettings(width, height, pixelFormat), allowPrototypeEncoding, formatConverter)
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        HardwareVideoEncoderSettings settings,
        bool allowPrototypeEncoding,
        IHardwareEncoderFormatConverter? formatConverter)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
        _allowPrototypeEncoding = allowPrototypeEncoding;
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
            RequiresGpuSurface = true
        };
    }

    public MediaFoundationHardwareVideoEncoder(
        int width,
        int height,
        string pixelFormat = "NV12")
        : this(CreateDefaultDevice(), CreateSettings(width, height, pixelFormat), allowPrototypeEncoding: false)
    {
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

        if (context.InputLease.BackendSurface is not D3D11SharedTextureFrameHandle surface)
        {
            if (!_allowPrototypeEncoding)
            {
                throw MediaFoundationHardwareH264EncoderSession.CreateUnavailableException(
                    new InvalidOperationException("Encoder requires a D3D11 shared texture backend surface."));
            }

            throw new InvalidOperationException("Encoder requires a D3D11 shared texture backend surface.");
        }

        if (!_allowPrototypeEncoding)
        {
            var result = EnsureProductBackendAvailable()
                .TryEncodeSurface(
                    surface,
                    Interlocked.Increment(ref _frameNumber),
                    context.PresentationTime,
                    auditSink);

            if (result is null)
                return ValueTask.FromResult<EncodedVideoPacket?>(null);

            return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
            {
                Data = result.Value.Data,
                Codec = EncodedVideoCodec.H264,
                BitstreamFormat = H264NalUtilities.ContainsValidStartCode(result.Value.Data.Span)
                    ? EncodedVideoBitstreamFormat.AnnexB
                    : EncodedVideoBitstreamFormat.Avcc,
                PresentationTime = context.PresentationTime,
                Duration = FrameDuration,
                IsKeyFrame = result.Value.IsKeyFrame,
                CodecConfiguration = result.Value.CodecConfiguration,
                Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
                    nameof(MediaFoundationHardwareVideoEncoder),
                    _info.Backend,
                    MediaForgeCapabilityCatalog.HardwareEncodeProof)
            });
        }

        _prototypeSession ??= CreatePrototypeSession();

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = nameof(MediaFoundationHardwareVideoEncoder),
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype,
            Detail = "Prototype encoder path accepted D3D11 GPU surface input; real MF MFT output validation is not implemented yet."
        });

        var encoded = _prototypeSession.TryEncodeSurface(surface, Interlocked.Increment(ref _frameNumber));
        if (encoded is null)
            return ValueTask.FromResult<EncodedVideoPacket?>(null);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = nameof(MediaFoundationHardwareVideoEncoder),
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype,
            Detail = $"Prototype H.264 packet produced ({encoded.Value.Data.Length} bytes)."
        });

        return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
        {
            Data = encoded.Value.Data,
            Codec = EncodedVideoCodec.H264,
            BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
            PresentationTime = context.PresentationTime,
            Duration = FrameDuration,
            IsKeyFrame = encoded.Value.IsKeyFrame,
            CodecConfiguration = encoded.Value.CodecConfiguration,
            Evidence = EncodedVideoPacketEvidence.CreatePrototype(
                nameof(MediaFoundationHardwareVideoEncoder),
                _info.Backend)
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

        if (!_allowPrototypeEncoding)
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
        _prototypeSession?.Dispose();
        _prototypeSession = null;
        _hardwareSession?.Dispose();
        _hardwareSession = null;
        return ValueTask.CompletedTask;
    }

    private MediaFoundationHardwareH264EncoderSession EnsureProductBackendAvailable()
    {
        _hardwareSession ??= new MediaFoundationHardwareH264EncoderSession(
            _device,
            _settings);
        _hardwareSession.Initialize();
        return _hardwareSession;
    }

    private PrototypeMediaFoundationH264EncoderSession CreatePrototypeSession()
    {
        var session = new PrototypeMediaFoundationH264EncoderSession(
            _device,
            _inputRequirement.Width,
            _inputRequirement.Height);
        session.Initialize();
        return session;
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

    private static ID3D11Device CreateDefaultDevice()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation hardware encoder requires Windows.");

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);
        return gpuDevice.Device;
    }
}
