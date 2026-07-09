using Vortice.Direct3D11;
using Vortice.DXGI;
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
    private readonly GpuResourcePool _resourcePool;
    private readonly bool _allowPrototypeDecoding;
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
        _gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);
        _resourcePool = new GpuResourcePool(new WindowsDecodeGpuTextureFactory(_gpuDevice.Device));
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
        _resourcePool = new GpuResourcePool(textureFactory);
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
        {
            throw new NotSupportedException(
                "MediaFoundationHardwareVideoDecoder is prototype-only until real MF/D3D11VA decode is implemented.");
        }

        if (!File.Exists(context.SourcePath))
            throw new FileNotFoundException("Video file was not found.", context.SourcePath);

        if (!MediaFoundationDecodeBridge.TryOpen(context.SourcePath, Device, out var width, out var height))
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

        if (_sourcePath is null)
            return ValueTask.FromResult<DecodedGpuFrame?>(null);

        if (!MediaFoundationDecodeBridge.TryReadGpuFrame(_sourcePath))
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
        _resourcePool.Dispose();
        _gpuDevice.Dispose();
        MediaFoundationDecodeBridge.Reset();
        return ValueTask.CompletedTask;
    }
}

internal sealed class WindowsDecodeGpuTextureFactory : IGpuTextureFactory
{
    private readonly ID3D11Device _device;

    public WindowsDecodeGpuTextureFactory(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor) =>
        new WindowsDecodeGpuPhysicalTexture(
            _device,
            D3D11SharedTextureFactory.CreateSharedTexture(
                _device,
                (uint)descriptor.Width,
                (uint)descriptor.Height));
}

internal sealed class WindowsDecodeGpuPhysicalTexture : IGpuPhysicalResource
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

    public bool TryFinalizePhysicalResources()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return _fullyDisposed.Task.IsCompleted;

        _handle.Dispose();
        _fullyDisposed.TrySetResult();
        return true;
    }
}

internal static class MediaFoundationDecodeBridge
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
