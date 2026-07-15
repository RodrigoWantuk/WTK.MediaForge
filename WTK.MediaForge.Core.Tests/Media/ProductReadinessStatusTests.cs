using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class ProductReadinessStatusTests
{
    [Fact]
    public void Current_unproven_media_paths_are_not_product_available_even_after_export_proof()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            ExportProofStatus = GpuExportProofStatus.Passed
        });

        AssertUnprovenUnavailable(report, MediaForgeCapabilityCatalog.RecordingMp4H264);
        AssertUnprovenUnavailable(report, MediaForgeCapabilityCatalog.RtmpH264);
        AssertUnprovenUnavailable(report, MediaForgeCapabilityCatalog.MfHardwareH264);
        AssertUnprovenUnavailable(report, MediaForgeCapabilityCatalog.VideoFileMp4);
    }

    [Fact]
    public void Export_surface_proof_is_backend_call_evidence_not_product_validation()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            ExportProofStatus = GpuExportProofStatus.Passed
        });

        var entry = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.GpuExportProof));

        Assert.Equal(MediaForgeSupportStatus.Supported, entry.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.BackendCallSucceeded, entry.ProductReadinessStatus);
        Assert.NotEqual(MediaForgeProductReadinessStatus.ProductValidated, entry.ProductReadinessStatus);
    }

    [Fact]
    public void Synthetic_performance_baseline_is_skeleton_and_not_user_available()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test"
        });

        var entry = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.EnginePerformanceBaseline));

        Assert.Equal(MediaForgeSupportStatus.Deferred, entry.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Skeleton, entry.ProductReadinessStatus);
        Assert.False(report.IsFeatureAvailable(entry.Id));
        Assert.Contains("Synthetic", entry.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_report_rejects_available_prototype_or_skeleton_entries()
    {
        AssertUnavailableReadinessRejected(
            MediaForgeProductReadinessStatus.Prototype,
            MediaForgeSupportStatus.Supported);

        AssertUnavailableReadinessRejected(
            MediaForgeProductReadinessStatus.Skeleton,
            MediaForgeSupportStatus.Experimental);
    }

    [Fact]
    public void Prototype_audit_evidence_does_not_satisfy_product_proof()
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
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = "prototype-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = "prototype-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });

        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
    }

    private static void AssertUnprovenUnavailable(MediaForgeCapabilityReport report, string id)
    {
        var entry = Assert.IsType<CapabilityEntry>(report.TryGetEntry(id));

        Assert.Equal(MediaForgeSupportStatus.Unavailable, entry.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Contract, entry.ProductReadinessStatus);
        Assert.False(report.IsFeatureAvailable(id));
        Assert.False(string.IsNullOrWhiteSpace(entry.UnavailableReason));
    }

    private static void AssertUnavailableReadinessRejected(
        MediaForgeProductReadinessStatus readiness,
        MediaForgeSupportStatus support)
    {
        var entry = new CapabilityEntry
        {
            Id = $"test.{readiness.ToString().ToLowerInvariant()}",
            Category = CapabilityCategories.Sink,
            DisplayName = readiness.ToString(),
            SupportStatus = support,
            LicenseStatus = MediaForgeLicenseStatus.Approved,
            ProductReadinessStatus = readiness
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(
                new HardwareMediaCapabilityReport { Platform = "Test" },
                [entry]));

        Assert.Contains(entry.Id, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(readiness.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
