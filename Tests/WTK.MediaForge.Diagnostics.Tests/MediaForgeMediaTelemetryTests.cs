using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Diagnostics.Tests;

public class MediaForgeMediaTelemetryTests
{
    [Fact]
    public void Telemetry_records_counters_and_snapshot()
    {
        var telemetry = new MediaForgeMediaTelemetry();

        telemetry.RecordGpuFrameComposed();
        telemetry.RecordEncodedPacketProduced();
        telemetry.RecordRawCpuVideoException();
        telemetry.RecordDebugReadbackFrame();
        telemetry.RecordHardwareEncoderFrame(TimeSpan.FromMilliseconds(2));
        telemetry.RecordHardwareDecoderFrame();
        telemetry.RecordFrameDropped();
        telemetry.RecordMuxerLatency(TimeSpan.FromMilliseconds(1));
        telemetry.RecordSourceBufferDepth(4);

        var snapshot = telemetry.Snapshot();

        Assert.Equal(1, snapshot.GpuFramesComposed);
        Assert.Equal(1, snapshot.EncodedPacketsProduced);
        Assert.Equal(1, snapshot.RawCpuVideoExceptions);
        Assert.Equal(1, snapshot.DebugReadbackFrames);
        Assert.Equal(1, snapshot.HardwareEncoderFrames);
        Assert.Equal(1, snapshot.HardwareDecoderFrames);
        Assert.Equal(1, snapshot.FramesDropped);
        Assert.Equal(TimeSpan.FromMilliseconds(2), snapshot.EncoderLatencyTotal);
        Assert.Equal(TimeSpan.FromMilliseconds(1), snapshot.MuxerLatencyTotal);
        Assert.Equal(4, snapshot.SourceBufferDepthMax);
    }
}
