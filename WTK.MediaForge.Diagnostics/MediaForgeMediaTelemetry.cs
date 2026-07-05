namespace WTK.MediaForge.Diagnostics;

public sealed class MediaForgeMediaTelemetry
{
    private long _gpuFramesComposed;
    private long _encodedPacketsProduced;
    private long _rawCpuVideoExceptions;
    private long _debugReadbackFrames;
    private long _hardwareEncoderFrames;
    private long _hardwareDecoderFrames;
    private long _framesDropped;
    private TimeSpan _encoderLatencyTotal;
    private TimeSpan _muxerLatencyTotal;
    private int _sourceBufferDepthMax;

    public long GpuFramesComposed => Volatile.Read(ref _gpuFramesComposed);

    public long EncodedPacketsProduced => Volatile.Read(ref _encodedPacketsProduced);

    public long RawCpuVideoExceptions => Volatile.Read(ref _rawCpuVideoExceptions);

    public long DebugReadbackFrames => Volatile.Read(ref _debugReadbackFrames);

    public long HardwareEncoderFrames => Volatile.Read(ref _hardwareEncoderFrames);

    public long HardwareDecoderFrames => Volatile.Read(ref _hardwareDecoderFrames);

    public long FramesDropped => Volatile.Read(ref _framesDropped);

    public TimeSpan EncoderLatencyTotal
    {
        get
        {
            lock (this)
                return _encoderLatencyTotal;
        }
    }

    public TimeSpan MuxerLatencyTotal
    {
        get
        {
            lock (this)
                return _muxerLatencyTotal;
        }
    }

    public int SourceBufferDepthMax => Volatile.Read(ref _sourceBufferDepthMax);

    public void RecordGpuFrameComposed() =>
        Interlocked.Increment(ref _gpuFramesComposed);

    public void RecordEncodedPacketProduced() =>
        Interlocked.Increment(ref _encodedPacketsProduced);

    public void RecordRawCpuVideoException() =>
        Interlocked.Increment(ref _rawCpuVideoExceptions);

    public void RecordDebugReadbackFrame() =>
        Interlocked.Increment(ref _debugReadbackFrames);

    public void RecordHardwareEncoderFrame(TimeSpan latency)
    {
        Interlocked.Increment(ref _hardwareEncoderFrames);
        lock (this)
            _encoderLatencyTotal += latency;
    }

    public void RecordHardwareDecoderFrame() =>
        Interlocked.Increment(ref _hardwareDecoderFrames);

    public void RecordFrameDropped() =>
        Interlocked.Increment(ref _framesDropped);

    public void RecordMuxerLatency(TimeSpan latency)
    {
        lock (this)
            _muxerLatencyTotal += latency;
    }

    public void RecordSourceBufferDepth(int depth)
    {
        int current;
        int updated;
        do
        {
            current = _sourceBufferDepthMax;
            updated = Math.Max(current, depth);
        }
        while (Interlocked.CompareExchange(ref _sourceBufferDepthMax, updated, current) != current);
    }

    public MediaForgeMediaTelemetrySnapshot Snapshot() => new(
        GpuFramesComposed: GpuFramesComposed,
        EncodedPacketsProduced: EncodedPacketsProduced,
        RawCpuVideoExceptions: RawCpuVideoExceptions,
        DebugReadbackFrames: DebugReadbackFrames,
        HardwareEncoderFrames: HardwareEncoderFrames,
        HardwareDecoderFrames: HardwareDecoderFrames,
        FramesDropped: FramesDropped,
        EncoderLatencyTotal: EncoderLatencyTotal,
        MuxerLatencyTotal: MuxerLatencyTotal,
        SourceBufferDepthMax: SourceBufferDepthMax);
}

public readonly record struct MediaForgeMediaTelemetrySnapshot(
    long GpuFramesComposed,
    long EncodedPacketsProduced,
    long RawCpuVideoExceptions,
    long DebugReadbackFrames,
    long HardwareEncoderFrames,
    long HardwareDecoderFrames,
    long FramesDropped,
    TimeSpan EncoderLatencyTotal,
    TimeSpan MuxerLatencyTotal,
    int SourceBufferDepthMax);
