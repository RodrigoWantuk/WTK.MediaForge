using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class ProductMediaPathsDoNotUsePrototypeEvidenceTests
{
    private static readonly string[] PrototypeProductCapabilityIds =
    [
        MediaForgeCapabilityCatalog.RecordingMp4H264,
        MediaForgeCapabilityCatalog.RtmpH264,
        MediaForgeCapabilityCatalog.MfHardwareH264,
        MediaForgeCapabilityCatalog.VideoFileMp4
    ];

    [Fact]
    public void Prototype_product_media_capabilities_are_not_available_even_after_export_proof_passes()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            ExportProofStatus = GpuExportProofStatus.Passed
        });

        Assert.All(PrototypeProductCapabilityIds, id =>
        {
            var entry = Assert.IsType<CapabilityEntry>(report.TryGetEntry(id));

            Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, entry.SupportStatus);
            Assert.Equal(MediaForgeProductReadinessStatus.Prototype, entry.ProductReadinessStatus);
            Assert.False(report.IsFeatureAvailable(id));
            Assert.False(IsUserAvailable(entry.SupportStatus));
            Assert.False(string.IsNullOrWhiteSpace(entry.UnavailableReason));
        });
    }

    [Fact]
    public void Prototype_evidence_never_satisfies_product_export_or_decode_proofs()
    {
        var exportAudit = new CollectingMediaTransportAuditSink();
        exportAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = "exporter",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        exportAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = "exporter",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });
        exportAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface,
            Source = "prototype-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });
        exportAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.EncodedPacketProduced,
            Source = "prototype-encoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });

        var decodeAudit = new CollectingMediaTransportAuditSink();
        decodeAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareDecodeSucceeded,
            Source = "prototype-decoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.Prototype
        });

        Assert.True(MediaTransportAuditRules.IsProductPathValid(exportAudit.Events));
        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(exportAudit.Events));
        Assert.False(MediaTransportAuditRules.IsDecodePathValid(decodeAudit.Events));
        Assert.False(MediaTransportAuditRules.IsDecodeToRenderProofPathValid(decodeAudit.Events));
    }

    [Fact]
    public void Decode_to_render_proof_requires_backend_validated_decode_adapter_and_render_evidence()
    {
        var prototypeDecode = CreateDecodeToRenderAudit(MediaTransportAuditEvidenceKind.Prototype);
        Assert.False(MediaTransportAuditRules.IsDecodeToRenderProofPathValid(prototypeDecode.Events));

        var missingRendererEvidence = new CollectingMediaTransportAuditSink();
        missingRendererEvidence.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareDecodeSucceeded,
            Source = "real-decoder",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated
        });
        missingRendererEvidence.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.DecodedFrameAdaptedToSourceFrame,
            Source = "adapter",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendOutputValidated
        });

        Assert.False(MediaTransportAuditRules.IsDecodeToRenderProofPathValid(missingRendererEvidence.Events));

        var readbackAudit = CreateDecodeToRenderAudit(MediaTransportAuditEvidenceKind.BackendOutputValidated);
        readbackAudit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.CpuReadbackAttempted,
            Source = "test",
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
        });

        Assert.False(MediaTransportAuditRules.IsDecodeToRenderProofPathValid(readbackAudit.Events));

        var valid = CreateDecodeToRenderAudit(MediaTransportAuditEvidenceKind.BackendOutputValidated);
        Assert.True(MediaTransportAuditRules.IsDecodeToRenderProofPathValid(valid.Events));
    }

    [Fact]
    public void Duplicate_capability_ids_are_rejected_before_conflicting_status_can_reach_ui()
    {
        var duplicate = new CapabilityEntry
        {
            Id = MediaForgeCapabilityCatalog.RecordingMp4H264,
            Category = CapabilityCategories.Sink,
            DisplayName = "Conflicting MP4 recording",
            SupportStatus = MediaForgeSupportStatus.Experimental,
            LicenseStatus = MediaForgeLicenseStatus.Approved,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.ProductValidated
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(
                new HardwareMediaCapabilityReport { Platform = "Test" },
                [duplicate]));

        Assert.Contains("duplicate capability id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(MediaForgeCapabilityCatalog.RecordingMp4H264, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserAvailable(MediaForgeSupportStatus status) =>
        status is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental;

    private static CollectingMediaTransportAuditSink CreateDecodeToRenderAudit(MediaTransportAuditEvidenceKind evidenceKind)
    {
        var audit = new CollectingMediaTransportAuditSink();
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareDecodeSucceeded,
            Source = "decoder",
            EvidenceKind = evidenceKind
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.DecodedFrameAdaptedToSourceFrame,
            Source = "adapter",
            EvidenceKind = evidenceKind
        });
        audit.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.SourceFrameSubmittedToRenderer,
            Source = "renderer",
            EvidenceKind = evidenceKind
        });

        return audit;
    }
}
