using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal interface IRenderedOutputEncoderSurfaceExporter
{
    bool CanExport(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement);

    ValueTask<HardwareEncoderInputLease> ExportAsync(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken);
}

internal sealed class RenderedOutputEncodeFrameAdapter
{
    private readonly IRenderedOutputEncoderSurfaceExporter _surfaceExporter;

    public RenderedOutputEncodeFrameAdapter(IRenderedOutputEncoderSurfaceExporter surfaceExporter) =>
        _surfaceExporter = surfaceExporter ?? throw new ArgumentNullException(nameof(surfaceExporter));

    public async ValueTask<ScheduledRenderedFrame> CreateScheduledFrameAsync(
        RenderedOutputFrame frame,
        FrameExecutionContext context,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_surfaceExporter.CanExport(frame.SurfaceLease, requirement))
        {
            throw new NotSupportedException(
                $"Rendered output {frame.OutputId} cannot be exported to the hardware encoder without a GPU-only surface path.");
        }

        var encoderInputLease = await _surfaceExporter
            .ExportAsync(frame.SurfaceLease, requirement, auditSink, cancellationToken)
            .ConfigureAwait(false);

        return new ScheduledRenderedFrame
        {
            Context = context,
            EncoderInputLease = encoderInputLease
        };
    }
}
