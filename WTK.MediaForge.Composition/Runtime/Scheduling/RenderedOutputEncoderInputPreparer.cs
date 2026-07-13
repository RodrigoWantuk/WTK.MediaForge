using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal interface IRenderedOutputEncoderInputPreparer
{
    ValueTask<HardwareEncoderInputLease> PrepareAsync(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken);
}

internal interface IRenderedOutputEncoderInputConverter
{
    bool CanConvert(
        HardwareEncoderInputLease source,
        HardwareEncoderInputRequirement requirement);

    ValueTask<HardwareEncoderInputLease> ConvertAsync(
        HardwareEncoderInputLease source,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken);
}

internal sealed class RenderedOutputEncoderInputPreparer : IRenderedOutputEncoderInputPreparer
{
    private readonly IRenderedOutputEncoderSurfaceExporter _surfaceExporter;
    private readonly IRenderedOutputEncoderInputConverter? _inputConverter;

    public RenderedOutputEncoderInputPreparer(
        IRenderedOutputEncoderSurfaceExporter surfaceExporter,
        IRenderedOutputEncoderInputConverter? inputConverter = null)
    {
        _surfaceExporter = surfaceExporter ?? throw new ArgumentNullException(nameof(surfaceExporter));
        _inputConverter = inputConverter;
    }

    public async ValueTask<HardwareEncoderInputLease> PrepareAsync(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (_surfaceExporter.CanExport(surface, requirement))
        {
            return await _surfaceExporter
                .ExportAsync(surface, requirement, auditSink, cancellationToken)
                .ConfigureAwait(false);
        }

        var sourceRequirement = CreateSourceRequirement(surface, requirement);
        if (!_surfaceExporter.CanExport(surface, sourceRequirement))
        {
            throw new NotSupportedException(
                $"Rendered output {surface.OutputId} cannot be exported to a GPU encoder input surface without a GPU-only path; CPU staging is prohibited.");
        }

        var sourceLease = await _surfaceExporter
            .ExportAsync(surface, sourceRequirement, auditSink, cancellationToken)
            .ConfigureAwait(false);

        var sourceLeaseReleased = false;
        try
        {
            if (_inputConverter is null || !_inputConverter.CanConvert(sourceLease, requirement))
            {
                auditSink.Record(new MediaTransportAuditEvent
                {
                    Kind = MediaTransportAuditEventKind.GpuFormatConversionUnavailable,
                    Source = nameof(RenderedOutputEncoderInputPreparer),
                    EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
                    Detail = $"No GPU-only conversion path is available from {sourceLease.Descriptor.Format} to {requirement.PixelFormat}."
                });

                throw new NotSupportedException(
                    $"Rendered output {surface.OutputId} requires GPU format conversion from {sourceLease.Descriptor.Format} to {requirement.PixelFormat}, but no compatible converter is available.");
            }

            var converted = await _inputConverter
                .ConvertAsync(sourceLease, requirement, auditSink, cancellationToken)
                .ConfigureAwait(false);

            if (ReferenceEquals(converted, sourceLease))
            {
                throw new InvalidOperationException(
                    "GPU encoder input conversion must return a new lease; in-place conversion would make source and converted lifetimes ambiguous.");
            }

            if (!string.Equals(converted.Descriptor.Format, requirement.PixelFormat, StringComparison.OrdinalIgnoreCase) ||
                converted.Descriptor.Width != requirement.Width ||
                converted.Descriptor.Height != requirement.Height ||
                converted.Descriptor.TransportKind != MediaTransportKind.GpuSurface)
            {
                converted.Dispose();
                throw new InvalidOperationException(
                    $"GPU encoder input converter returned an incompatible surface ({converted.Descriptor.Width}x{converted.Descriptor.Height} {converted.Descriptor.Format}).");
            }

            sourceLease.Dispose();
            sourceLeaseReleased = true;
            return converted;
        }
        catch
        {
            if (!sourceLeaseReleased)
                sourceLease.Dispose();

            throw;
        }
    }

    private static HardwareEncoderInputRequirement CreateSourceRequirement(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement finalRequirement) =>
        new()
        {
            Width = finalRequirement.Width,
            Height = finalRequirement.Height,
            PixelFormat = ToEncoderSurfaceFormat(surface.Format),
            RequiresGpuSurface = finalRequirement.RequiresGpuSurface
        };

    private static string ToEncoderSurfaceFormat(RenderPixelFormat format) =>
        format switch
        {
            RenderPixelFormat.Rgba8Unorm => "B8G8R8A8_UNORM",
            _ => format.ToString()
        };
}
