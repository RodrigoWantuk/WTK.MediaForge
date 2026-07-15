using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media.Encode;

namespace WTK.MediaForge.Windows.Media.Interop;

internal sealed class WindowsRenderedOutputEncoderInputConverter : IRenderedOutputEncoderInputConverter
{
    private readonly ID3D11Device _device;

    public WindowsRenderedOutputEncoderInputConverter(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public bool CanConvert(
        HardwareEncoderInputLease source,
        HardwareEncoderInputRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requirement);

        return source.BackendSurface is D3D11SharedTextureFrameHandle &&
               source.Descriptor.TransportKind == MediaTransportKind.GpuSurface &&
               requirement.RequiresGpuSurface &&
               source.Descriptor.Width == requirement.Width &&
               source.Descriptor.Height == requirement.Height &&
               D3D11BgraToNv12Converter.IsBgra(source.Descriptor.Format) &&
               D3D11BgraToNv12Converter.IsNv12(requirement.PixelFormat);
    }

    public ValueTask<HardwareEncoderInputLease> ConvertAsync(
        HardwareEncoderInputLease source,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanConvert(source, requirement))
        {
            RecordUnavailable(
                auditSink,
                $"Rendered output encoder conversion cannot convert {source.Descriptor.Format} to {requirement.PixelFormat} without a D3D11 shared texture path.");

            throw new NotSupportedException(
                "Rendered output encoder conversion requires a D3D11 shared texture BGRA/RGBA source and NV12 GPU target.");
        }

        var sourceHandle = (D3D11SharedTextureFrameHandle)source.BackendSurface!;
        ID3D11Texture2D? outputTexture = null;

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionStarted,
            Source = nameof(WindowsRenderedOutputEncoderInputConverter),
            EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
            Detail = "Starting D3D11 VideoProcessor conversion from rendered output surface to NV12 encoder input."
        });

        try
        {
            outputTexture = D3D11BgraToNv12Converter.CreatePrivateVideoProcessorTexture(
                _device,
                Format.NV12,
                requirement.Width,
                requirement.Height,
                BindFlags.RenderTarget);

            D3D11BgraToNv12Converter.ExecuteVideoProcessorConversion(
                _device,
                sourceHandle.Texture,
                outputTexture,
                requirement.Width,
                requirement.Height,
                requirement.FramesPerSecond,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outputTexture?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            outputTexture?.Dispose();
            RecordUnavailable(auditSink, $"D3D11 rendered-output to NV12 conversion failed: {ex.Message}");
            throw new NotSupportedException(
                "D3D11 rendered-output to NV12 conversion failed on the current GPU/driver.",
                ex);
        }

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionSucceeded,
            Source = nameof(WindowsRenderedOutputEncoderInputConverter),
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
            Detail = "D3D11 VideoProcessor produced an NV12 hardware encoder input surface from rendered output without CPU staging."
        });

        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = requirement.Width,
            Height = requirement.Height,
            Format = requirement.PixelFormat,
            TransportKind = MediaTransportKind.GpuSurface
        };

        var lease = HardwareEncoderInputLease.CreateWithBackendSurface(
            descriptor,
            outputTexture,
            outputTexture.Dispose);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = nameof(WindowsRenderedOutputEncoderInputConverter),
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded,
            Detail = "NV12 rendered-output encoder input lease created."
        });

        return ValueTask.FromResult(lease);
    }

    private static void RecordUnavailable(
        IMediaTransportAuditSink auditSink,
        string detail)
    {
        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionUnavailable,
            Source = nameof(WindowsRenderedOutputEncoderInputConverter),
            EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
            Detail = detail
        });
    }
}
