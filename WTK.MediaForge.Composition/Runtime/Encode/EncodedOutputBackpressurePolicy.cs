using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal sealed class EncodedOutputBackpressurePolicy
{
    private EncodedOutputBackpressurePolicy(
        string name,
        EncodeSchedulerBackpressurePolicy encodeQueuePolicy,
        EncodedPacketConsumerBackpressurePolicy sinkPolicy,
        bool allowFrameDrop,
        bool failOnBackpressure,
        TimeSpan sinkWriteTimeout)
    {
        Name = name;
        EncodeQueuePolicy = encodeQueuePolicy;
        SinkPolicy = sinkPolicy;
        AllowFrameDrop = allowFrameDrop;
        FailOnBackpressure = failOnBackpressure;
        SinkWriteTimeout = sinkWriteTimeout;
    }

    public string Name { get; }

    public EncodeSchedulerBackpressurePolicy EncodeQueuePolicy { get; }

    public EncodedPacketConsumerBackpressurePolicy SinkPolicy { get; }

    public bool AllowFrameDrop { get; }

    public bool FailOnBackpressure { get; }

    public TimeSpan SinkWriteTimeout { get; }

    public static EncodedOutputBackpressurePolicy Recording() =>
        new(
            "Recording",
            EncodeSchedulerBackpressurePolicy.QueueWithBackpressure,
            EncodedPacketConsumerBackpressurePolicy.Backpressure,
            allowFrameDrop: false,
            failOnBackpressure: true,
            sinkWriteTimeout: TimeSpan.FromSeconds(10));

    public static EncodedOutputBackpressurePolicy Streaming() =>
        new(
            "Streaming",
            EncodeSchedulerBackpressurePolicy.KeepLatest,
            EncodedPacketConsumerBackpressurePolicy.FailOutput,
            allowFrameDrop: true,
            failOnBackpressure: false,
            sinkWriteTimeout: TimeSpan.FromSeconds(5));

    public static EncodedOutputBackpressurePolicy Diagnostics() =>
        new(
            "Diagnostics",
            EncodeSchedulerBackpressurePolicy.KeepLatest,
            EncodedPacketConsumerBackpressurePolicy.FailOutput,
            allowFrameDrop: true,
            failOnBackpressure: false,
            sinkWriteTimeout: TimeSpan.FromSeconds(5));

    public static EncodedOutputBackpressurePolicy ForOutputType(RenderOutputTypeId typeId)
    {
        if (typeId == RenderOutputTypes.RecordingMp4 || typeId == RenderOutputTypes.EncodedFile)
            return Recording();

        if (typeId == RenderOutputTypes.StreamingRtmp)
            return Streaming();

        return Diagnostics();
    }
}
