using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Outputs;
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
    private readonly IRenderedOutputEncoderInputPreparer _inputPreparer;

    public RenderedOutputEncodeFrameAdapter(IRenderedOutputEncoderSurfaceExporter surfaceExporter) =>
        _inputPreparer = new RenderedOutputEncoderInputPreparer(surfaceExporter);

    public RenderedOutputEncodeFrameAdapter(IRenderedOutputEncoderInputPreparer inputPreparer) =>
        _inputPreparer = inputPreparer ?? throw new ArgumentNullException(nameof(inputPreparer));

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

        var encoderInputLease = await _inputPreparer
            .PrepareAsync(frame.SurfaceLease, requirement, auditSink, cancellationToken)
            .ConfigureAwait(false);

        return new ScheduledRenderedFrame
        {
            Context = context,
            EncoderInputLease = encoderInputLease
        };
    }

    public async ValueTask<ScheduledRenderedFrame> CreateScheduledFrameAsync(
        RenderOutputFrameLease lease,
        FrameExecutionContext context,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        var surface = lease.SurfaceLease;
        if (surface is null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw new NotSupportedException(
                $"Rendered output {lease.OutputId} does not expose a backend surface lease for hardware encoding.");
        }

        try
        {
            var encoderInputLease = await _inputPreparer
                .PrepareAsync(surface, requirement, auditSink, cancellationToken)
                .ConfigureAwait(false);

            return new ScheduledRenderedFrame
            {
                Context = context,
                EncoderInputLease = encoderInputLease
            };
        }
        finally
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }
}
