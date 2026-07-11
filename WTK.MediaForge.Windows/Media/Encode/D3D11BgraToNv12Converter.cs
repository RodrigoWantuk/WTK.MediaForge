using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class D3D11BgraToNv12Converter : IHardwareEncoderFormatConverter
{
    private readonly ID3D11Device? _device;

    public D3D11BgraToNv12Converter()
    {
    }

    public D3D11BgraToNv12Converter(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public bool CanConvert(GpuVideoFrameDescriptor source, HardwareEncoderInputRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requirement);

        var isSupportedRequest = source.TransportKind == MediaTransportKind.GpuSurface
            && requirement.RequiresGpuSurface
            && source.Width == requirement.Width
            && source.Height == requirement.Height
            && IsBgra(source.Format)
            && IsNv12(requirement.PixelFormat);

        return isSupportedRequest && _device is not null;
    }

    public ValueTask<HardwareEncoderInputLease> ConvertAsync(
        GpuTextureLease sourceTexture,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTexture);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = sourceTexture.ToGpuVideoFrameDescriptor();
        if (!CanConvert(descriptor, requirement))
        {
            RecordUnavailable(
                auditSink,
                "BGRA/RGBA to NV12 conversion requires a matching D3D11 GPU device, dimensions, and NV12 encoder input.");

            throw new NotSupportedException(
                "D3D11 BGRA/RGBA to NV12 encoder format conversion requires a matching D3D11 GPU conversion path.");
        }

        if (sourceTexture.Texture.Physical is not IGpuFrameHandleProvider { FrameHandle: D3D11SharedTextureFrameHandle sourceHandle })
        {
            RecordUnavailable(
                auditSink,
                "Source GPU texture does not expose a D3D11 shared texture handle; CPU staging fallback is prohibited.");

            throw new NotSupportedException(
                "D3D11 BGRA/RGBA to NV12 conversion requires a D3D11 shared texture source.");
        }

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionStarted,
            Source = nameof(D3D11BgraToNv12Converter),
            EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
            Detail = "Starting D3D11 VideoProcessor BGRA/RGBA to NV12 conversion."
        });

        D3D11SharedTextureFrameHandle? outputHandle = null;
        try
        {
            outputHandle = D3D11SharedTextureFactory.CreateSharedTexture(
                _device!,
                (uint)requirement.Width,
                (uint)requirement.Height,
                Format.NV12);

            ExecuteVideoProcessorConversion(
                _device!,
                sourceHandle.Texture,
                outputHandle.Texture,
                requirement.Width,
                requirement.Height,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outputHandle?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            outputHandle?.Dispose();
            RecordUnavailable(
                auditSink,
                $"D3D11 VideoProcessor BGRA/RGBA to NV12 conversion failed: {ex.Message}");

            throw new NotSupportedException(
                "D3D11 BGRA/RGBA to NV12 encoder format conversion failed on the current GPU/driver.",
                ex);
        }

        var outputDescriptor = new GpuVideoFrameDescriptor
        {
            Width = requirement.Width,
            Height = requirement.Height,
            Format = requirement.PixelFormat,
            TransportKind = MediaTransportKind.GpuSurface
        };

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionSucceeded,
            Source = nameof(D3D11BgraToNv12Converter),
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
            Detail = "D3D11 VideoProcessor produced an NV12 encoder input surface without CPU staging."
        });

        var lease = HardwareEncoderInputLease.CreateWithBackendSurface(
            outputDescriptor,
            outputHandle!,
            outputHandle!.Dispose);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = nameof(D3D11BgraToNv12Converter),
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
            Detail = "NV12 GPU surface lease created for hardware encoder input."
        });

        return ValueTask.FromResult(lease);
    }

    private static void ExecuteVideoProcessorConversion(
        ID3D11Device device,
        ID3D11Texture2D source,
        ID3D11Texture2D output,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        using var immediateContext = device.ImmediateContext;
        using var videoContext = immediateContext.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputWidth = (uint)width,
            OutputHeight = (uint)height,
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
            Usage = VideoUsage.PlaybackNormal
        };

        videoDevice.CreateVideoProcessorEnumerator(ref content, out var enumerator).CheckError();
        using (enumerator)
        {
            videoDevice.CreateVideoProcessor(enumerator, 0, out var processor).CheckError();
            using (processor)
            {
                var inputDescription = new VideoProcessorInputViewDescription
                {
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView
                    {
                        MipSlice = 0,
                        ArraySlice = 0
                    }
                };
                videoDevice.CreateVideoProcessorInputView(
                    source,
                    enumerator,
                    inputDescription,
                    out var inputView).CheckError();

                using (inputView)
                {
                    var outputDescription = new VideoProcessorOutputViewDescription
                    {
                        ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                        Texture2D = new Texture2DVideoProcessorOutputView
                        {
                            MipSlice = 0
                        }
                    };
                    videoDevice.CreateVideoProcessorOutputView(
                        output,
                        enumerator,
                        outputDescription,
                        out var outputView).CheckError();

                    using (outputView)
                    {
                        var stream = new VideoProcessorStream
                        {
                            Enable = true,
                            OutputIndex = 0,
                            InputFrameOrField = 0,
                            InputSurface = inputView
                        };

                        videoContext.VideoProcessorBlt(
                            processor,
                            outputView,
                            outputFrame: 0,
                            streamCount: 1,
                            streams: [stream]).CheckError();
                    }
                }
            }
        }
    }

    private static void RecordUnavailable(
        IMediaTransportAuditSink auditSink,
        string detail)
    {
        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionUnavailable,
            Source = nameof(D3D11BgraToNv12Converter),
            EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
            Detail = detail
        });
    }

    private static bool IsBgra(string format) =>
        format.Equals("B8G8R8A8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("BGRA8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("Bgra8Unorm", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("R8G8B8A8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("RGBA8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("Rgba8Unorm", StringComparison.OrdinalIgnoreCase);

    private static bool IsNv12(string format) =>
        format.Equals("NV12", StringComparison.OrdinalIgnoreCase);
}
