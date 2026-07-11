using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal sealed class RenderedOutputEncodeSchedulerBridge
{
    private readonly RenderedOutputEncodeFrameAdapter _frameAdapter;
    private readonly EncodeSchedulerTarget _schedulerTarget;
    private readonly HardwareEncoderInputRequirement _inputRequirement;
    private readonly IMediaTransportAuditSink _auditSink;

    public RenderedOutputEncodeSchedulerBridge(
        RenderedOutputEncodeFrameAdapter frameAdapter,
        EncodeSchedulerTarget schedulerTarget,
        HardwareEncoderInputRequirement inputRequirement,
        IMediaTransportAuditSink auditSink)
    {
        _frameAdapter = frameAdapter ?? throw new ArgumentNullException(nameof(frameAdapter));
        _schedulerTarget = schedulerTarget ?? throw new ArgumentNullException(nameof(schedulerTarget));
        _inputRequirement = inputRequirement ?? throw new ArgumentNullException(nameof(inputRequirement));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
    }

    public async ValueTask SubmitRenderedFrameAsync(
        RenderedOutputFrame frame,
        FrameExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);

        var scheduledFrame = await _frameAdapter
            .CreateScheduledFrameAsync(
                frame,
                context,
                _inputRequirement,
                _auditSink,
                cancellationToken)
            .ConfigureAwait(false);

        _schedulerTarget.EnqueueRenderedFrame(scheduledFrame);
    }
}
