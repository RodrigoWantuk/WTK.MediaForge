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
        Assert.Contains(MediaForgeCapabilityCatalog.RenderToEncodeProof, passed.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_proof_aggregator_promotes_recording_streaming_and_video_input_only_after_required_proofs_pass()
    {
        var unavailable = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            Proofs =
            [
                UnavailableProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
                UnavailableProof(MediaForgeCapabilityCatalog.RenderToEncodeProof),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof),
                UnavailableProof(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof),
                UnavailableProof(MediaForgeCapabilityCatalog.HardwareDecodeProof),
                UnavailableProof(MediaForgeCapabilityCatalog.DecodeToRenderProof),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4InputProductProof)
            ]
        });

        Assert.Equal(
            MediaForgeSupportStatus.PrototypeOnly,
            unavailable.TryGetEntry(MediaForgeCapabilityCatalog.RecordingMp4H264)!.SupportStatus);
        Assert.Equal(
            MediaForgeSupportStatus.PrototypeOnly,
            unavailable.TryGetEntry(MediaForgeCapabilityCatalog.RtmpH264)!.SupportStatus);
        Assert.Equal(
            MediaForgeSupportStatus.PrototypeOnly,
            unavailable.TryGetEntry(MediaForgeCapabilityCatalog.VideoFileMp4)!.SupportStatus);

        var available = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            Proofs =
            [
                PassedProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
                PassedProof(MediaForgeCapabilityCatalog.RenderToEncodeProof, "BackendCallSucceeded"),
                PassedProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
                PassedProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof),
                PassedProof(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof),
                PassedProof(MediaForgeCapabilityCatalog.HardwareDecodeProof),
                PassedProof(MediaForgeCapabilityCatalog.DecodeToRenderProof),
                PassedProof(MediaForgeCapabilityCatalog.Mp4InputProductProof)
            ]
        });

        var recording = available.TryGetEntry(MediaForgeCapabilityCatalog.RecordingMp4H264)!;
        Assert.Equal(MediaForgeSupportStatus.Supported, recording.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, recording.ProductReadinessStatus);

        var rtmp = available.TryGetEntry(MediaForgeCapabilityCatalog.RtmpH264)!;
        Assert.Equal(MediaForgeSupportStatus.Experimental, rtmp.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, rtmp.ProductReadinessStatus);

        var video = available.TryGetEntry(MediaForgeCapabilityCatalog.VideoFileMp4)!;
        Assert.Equal(MediaForgeSupportStatus.Experimental, video.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, video.ProductReadinessStatus);
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
    public void Capability_report_includes_v8_media_proof_entries()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            Proofs =
            [
                new HardwareMediaProof
                {
                    Id = MediaForgeCapabilityCatalog.RenderToEncodeProof,
                    DisplayName = "Render to encode",
                    Status = HardwareMediaProofStatus.Passed,
                    Backend = "TestBackend",
                    Evidence = ["BackendOutputValidated"]
                },
                new HardwareMediaProof
                {
                    Id = MediaForgeCapabilityCatalog.HardwareDecodeProof,
                    DisplayName = "Decode",
                    Status = HardwareMediaProofStatus.Unavailable,
                    Reason = "No hardware decoder in test."
                }
            ]
        });

        var renderToEncode = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.RenderToEncodeProof));
        Assert.Equal(MediaForgeSupportStatus.Supported, renderToEncode.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.BackendCallSucceeded, renderToEncode.ProductReadinessStatus);

        var decode = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.HardwareDecodeProof));
        Assert.Equal(MediaForgeSupportStatus.Unsupported, decode.SupportStatus);
        Assert.Contains("No hardware decoder", decode.UnavailableReason, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.Mp4OutputProductProof));
        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.Mp4InputProductProof));
        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.WebcamInputProductProof));
        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof));
        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.NdiInputProductProof));
        Assert.NotNull(report.TryGetEntry(MediaForgeCapabilityCatalog.NdiOutputProductProof));
    }

    [Fact]
    public void Capability_report_rejects_non_passed_proof_without_reason()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs =
                [
                    new HardwareMediaProof
                    {
                        Id = MediaForgeCapabilityCatalog.HardwareEncodeProof,
                        DisplayName = "Encode",
                        Status = HardwareMediaProofStatus.Unavailable
                    }
                ]
            }));

        Assert.Contains(MediaForgeCapabilityCatalog.HardwareEncodeProof, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reason", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_report_rejects_passed_proof_without_backend_validated_evidence()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs =
                [
                    new HardwareMediaProof
                    {
                        Id = MediaForgeCapabilityCatalog.HardwareEncodeProof,
                        DisplayName = "Encode",
                        Status = HardwareMediaProofStatus.Passed,
                        Backend = "TestBackend"
                    }
                ]
            }));

        Assert.Contains(MediaForgeCapabilityCatalog.HardwareEncodeProof, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BackendOutputValidated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MediaForgeCapabilityCatalog.Mp4OutputProductProof)]
    [InlineData(MediaForgeCapabilityCatalog.Mp4InputProductProof)]
    [InlineData(MediaForgeCapabilityCatalog.WebcamInputProductProof)]
    [InlineData(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof)]
    [InlineData(MediaForgeCapabilityCatalog.NdiInputProductProof)]
    [InlineData(MediaForgeCapabilityCatalog.NdiOutputProductProof)]
    public void V8_media_io_product_proofs_require_backend_output_validated_evidence(string proofId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs =
                [
                    new HardwareMediaProof
                    {
                        Id = proofId,
                        DisplayName = proofId,
                        Status = HardwareMediaProofStatus.Passed,
                        Backend = "TestBackend",
                        Evidence = ["BackendCallSucceeded"]
                    }
                ]
            }));

        Assert.Contains(proofId, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BackendOutputValidated", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
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
                Assert.False(string.IsNullOrWhiteSpace(entry.UnavailableReason));
            });
    }

    [Fact]
    public async Task Hardware_media_proof_registry_updates_session_capability_report_after_proof_passes()
    {
        var baseline = new HardwareMediaCapabilityReport
        {
            Platform = "Test",
            Proofs =
            [
                new HardwareMediaProof
                {
                    Id = MediaForgeCapabilityCatalog.RtmpNetworkOutputProof,
                    DisplayName = "RTMP network proof",
                    Status = HardwareMediaProofStatus.Unavailable,
                    Reason = "Proof has not run."
                }
            ]
        };
        var before = MediaForgeCapabilityReportBuilder.Build(baseline);
        Assert.Equal(
            MediaForgeSupportStatus.Unsupported,
            before.TryGetEntry(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof)!.SupportStatus);

        var registry = new HardwareMediaProofRegistry();
        registry.Register(new PassingProofRunner(
            MediaForgeCapabilityCatalog.RtmpNetworkOutputProof,
            "RTMP network proof"));

        var results = await registry.RunAsync(baseline, CancellationToken.None);
        var updatedHardware = HardwareMediaProofRegistry.ApplyResults(baseline, results);
        var after = MediaForgeCapabilityReportBuilder.Build(updatedHardware);

        var proofEntry = after.TryGetEntry(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof)!;
        Assert.Equal(MediaForgeSupportStatus.Supported, proofEntry.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, proofEntry.ProductReadinessStatus);
        Assert.Contains("Proof passed", proofEntry.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PassingProofRunner(string id, string displayName)
        : HardwareMediaProofRunner(id, displayName)
    {
        public override ValueTask<HardwareMediaProofResult> RunAsync(
            HardwareMediaCapabilityReport baseline,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Passed(
                backend: "TestBackend",
                evidence: [nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated)]));
        }
    }

    private static HardwareMediaProof UnavailableProof(string id) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Status = HardwareMediaProofStatus.Unavailable,
            Reason = "Unavailable in unit test."
        };

    private static HardwareMediaProof PassedProof(
        string id,
        string evidence = nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated)) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Status = HardwareMediaProofStatus.Passed,
            Backend = "TestBackend",
            Evidence = [evidence]
        };
}
