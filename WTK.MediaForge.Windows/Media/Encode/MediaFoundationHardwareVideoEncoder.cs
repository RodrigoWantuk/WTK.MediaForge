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
    private readonly ID3D11Device _device;
    private readonly bool _allowPrototypeEncoding;
    private PrototypeMediaFoundationH264EncoderSession? _prototypeSession;
    private MediaFoundationHardwareH264EncoderSession? _hardwareSession;
    private long _frameNumber;
    private bool _disposed;

    public MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat = "B8G8R8A8_UNORM")
        : this(device, width, height, pixelFormat, allowPrototypeEncoding: false)
    {
    }

    internal MediaFoundationHardwareVideoEncoder(
        ID3D11Device device,
        int width,
        int height,
        string pixelFormat,
        bool allowPrototypeEncoding)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _allowPrototypeEncoding = allowPrototypeEncoding;
        _info = new HardwareEncoderInfo
        {
            Name = "Media Foundation H.264 Hardware MFT",
            Codec = EncodedVideoCodec.H264,
            Backend = "MediaFoundation-HardwareMft",
            AcceptsGpuSurfaceInput = true
        };

        _inputRequirement = new HardwareEncoderInputRequirement
        {
            Width = width,
            Height = height,
            PixelFormat = pixelFormat,
            RequiresGpuSurface = true
        };
    }

    public MediaFoundationHardwareVideoEncoder(
        int width,
        int height,
        string pixelFormat = "NV12")
        : this(CreateDefaultDevice(), width, height, pixelFormat, allowPrototypeEncoding: false)
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

        if (!_allowPrototypeEncoding)
            EnsureProductBackendAvailable();

        if (context.InputLease.BackendSurface is not D3D11SharedTextureFrameHandle surface)
            throw new InvalidOperationException("Encoder requires a D3D11 shared texture backend surface.");

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
            PresentationTime = context.PresentationTime,
            IsKeyFrame = encoded.Value.IsKeyFrame
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
        if (!frameExporter.CanExport(descriptor, _inputRequirement))
            throw new InvalidOperationException("GPU frame exporter cannot export the provided texture lease.");

        using var inputLease = await frameExporter
            .ExportForEncoderAsync(descriptor, auditSink, context.CancellationToken)
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

    private void EnsureProductBackendAvailable()
    {
        _hardwareSession ??= new MediaFoundationHardwareH264EncoderSession(
            _device,
            _inputRequirement.Width,
            _inputRequirement.Height,
            _inputRequirement.PixelFormat);
        _hardwareSession.Initialize();
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
