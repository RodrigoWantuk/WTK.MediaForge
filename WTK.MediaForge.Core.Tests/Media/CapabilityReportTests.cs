using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class CapabilityReportTests
{
    [Fact]
    public void Default_catalog_marks_libx264_as_prohibited()
    {
        var entry = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Pending)
            .First(e => e.Id == MediaForgeCapabilityCatalog.LibX264);

        Assert.Equal(MediaForgeSupportStatus.Prohibited, entry.SupportStatus);
        Assert.Equal(MediaForgeLicenseStatus.Prohibited, entry.LicenseStatus);
    }

    [Fact]
    public void Default_catalog_marks_recording_mp4_blocked_until_export_proof()
    {
        var pending = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Pending)
            .First(e => e.Id == MediaForgeCapabilityCatalog.RecordingMp4H264);
        Assert.Equal(MediaForgeSupportStatus.Blocked, pending.SupportStatus);

        var passed = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Passed)
            .First(e => e.Id == MediaForgeCapabilityCatalog.RecordingMp4H264);
        Assert.Equal(MediaForgeSupportStatus.Planned, passed.SupportStatus);
    }

    [Fact]
    public void Default_catalog_marks_ffmpeg_not_used_in_mvp()
    {
        var entry = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Pending)
            .First(e => e.Id == MediaForgeCapabilityCatalog.Ffmpeg);

        Assert.Equal(MediaForgeSupportStatus.NotUsedInMvp, entry.SupportStatus);
        Assert.Equal(MediaForgeLicenseStatus.NotUsedInMvp, entry.LicenseStatus);
    }

    [Fact]
    public void Default_catalog_marks_vendor_sdk_direct_as_planned()
    {
        var entries = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Pending);
        Assert.All(
            new[] { MediaForgeCapabilityCatalog.NvencDirect, MediaForgeCapabilityCatalog.QsvDirect, MediaForgeCapabilityCatalog.AmfDirect },
            id =>
            {
                var entry = entries.First(e => e.Id == id);
                Assert.Equal(MediaForgeSupportStatus.Planned, entry.SupportStatus);
                Assert.Equal(MediaForgeLicenseStatus.RequiresLegalReview, entry.LicenseStatus);
            });
    }

    [Fact]
    public void Default_catalog_marks_srt_as_planned_blocked()
    {
        var entry = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Pending)
            .First(e => e.Id == MediaForgeCapabilityCatalog.SrtOutput);

        Assert.Equal(MediaForgeSupportStatus.Planned, entry.SupportStatus);
        Assert.Contains("license", entry.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RawCpuVideoFrameExceptionKind_has_only_three_values()
    {
        var names = Enum.GetNames(typeof(RawCpuVideoFrameExceptionKind));
        Assert.Equal(3, names.Length);
        Assert.Contains(nameof(RawCpuVideoFrameExceptionKind.PixelTestOnly), names);
        Assert.Contains(nameof(RawCpuVideoFrameExceptionKind.ManualScreenshotOnly), names);
        Assert.Contains(nameof(RawCpuVideoFrameExceptionKind.WebcamSystemRawInput), names);
    }

    [Fact]
    public void MediaTransportAuditRules_fails_on_readback_or_staging()
    {
        var bad = new CollectingMediaTransportAuditSink();
        bad.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "test"
        });
        bad.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.CpuReadbackAttempted,
            Source = "test"
        });

        Assert.False(MediaTransportAuditRules.IsProductPathValid(bad.Events));
    }

    [Fact]
    public void MediaTransportAuditRules_passes_on_valid_gpu_path()
    {
        var good = new CollectingMediaTransportAuditSink();
        good.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "test"
        });
        good.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "test"
        });

        Assert.True(MediaTransportAuditRules.IsProductPathValid(good.Events));
    }
}
