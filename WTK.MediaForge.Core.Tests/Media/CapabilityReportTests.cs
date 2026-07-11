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
        Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, pending.SupportStatus);

        var passed = MediaForgeCapabilityCatalog.CreateDefaultEntries(GpuExportProofStatus.Passed)
            .First(e => e.Id == MediaForgeCapabilityCatalog.RecordingMp4H264);
        Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, passed.SupportStatus);
        Assert.Contains("Prototype", passed.UnavailableReason, StringComparison.OrdinalIgnoreCase);
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
    public void Capability_report_rejects_unavailable_entries_without_reason()
    {
        var entry = new CapabilityEntry
        {
            Id = "test.planned.missing_reason",
            Category = CapabilityCategories.Source,
            DisplayName = "Missing reason",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.Approved,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(
                new HardwareMediaCapabilityReport { Platform = "Test" },
                [entry]));

        Assert.Contains(entry.Id, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable reason", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_report_rejects_available_backend_that_requires_cpu_staging()
    {
        var backend = new HardwareMediaBackendCapability
        {
            Id = "test.software.decode",
            DisplayName = "Software decode",
            Platform = "Test",
            DecodeCodecs = ["H264"],
            RequiresCpuStaging = true,
            SupportStatus = MediaForgeSupportStatus.Experimental,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.ProductValidated
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(
                new HardwareMediaCapabilityReport
                {
                    Platform = "Test",
                    BackendCapabilities = [backend]
                }));

        Assert.Contains(backend.Id, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CPU staging", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_report_rejects_available_prototype_backend()
    {
        var backend = new HardwareMediaBackendCapability
        {
            Id = "test.prototype.encoder",
            DisplayName = "Prototype encoder",
            Platform = "Test",
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Supported,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Prototype
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(
                new HardwareMediaCapabilityReport
                {
                    Platform = "Test",
                    BackendCapabilities = [backend]
                }));

        Assert.Contains(backend.Id, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prototype", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
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
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        good.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });

        Assert.True(MediaTransportAuditRules.IsProductPathValid(good.Events));
    }

    [Fact]
    public void MediaTransportAuditRules_rejects_contract_only_export_as_product_path()
    {
        var audit = new CollectingMediaTransportAuditSink();
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "test"
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "test"
        });

        Assert.False(MediaTransportAuditRules.IsProductPathValid(audit.Events));
    }

    [Fact]
    public void Canned_h264_bytes_do_not_satisfy_export_proof()
    {
        var audit = new CollectingMediaTransportAuditSink();
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = "canned-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = "canned-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });

        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
    }

    [Fact]
    public void Backend_validated_encoder_output_satisfies_export_proof()
    {
        var audit = new CollectingMediaTransportAuditSink();
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = "real-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = "real-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated
        });

        Assert.True(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
    }

    [Fact]
    public void Shared_texture_creation_alone_does_not_satisfy_export_proof()
    {
        var audit = new CollectingMediaTransportAuditSink();
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "exporter",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "exporter",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });

        Assert.True(MediaTransportAuditRules.IsProductPathValid(audit.Events));
        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
    }

    [Fact]
    public void CapabilityReport_does_not_advertise_fake_media_paths_as_supported()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            ExportProofStatus = GpuExportProofStatus.Passed
        });

        Assert.All(
            new[]
            {
                MediaForgeCapabilityCatalog.RecordingMp4H264,
                MediaForgeCapabilityCatalog.RtmpH264,
                MediaForgeCapabilityCatalog.MfHardwareH264,
                MediaForgeCapabilityCatalog.VideoFileMp4
            },
            id =>
            {
                var entry = Assert.IsType<CapabilityEntry>(report.TryGetEntry(id));
                Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, entry.SupportStatus);
                Assert.False(report.IsFeatureAvailable(id));
                Assert.Contains("Prototype", entry.UnavailableReason, StringComparison.OrdinalIgnoreCase);
            });
    }
}
