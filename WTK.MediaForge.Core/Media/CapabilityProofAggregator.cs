namespace WTK.MediaForge.Core.Media;

public sealed class CapabilityProofAggregator
{
    private static readonly string[] RecordingMp4Proofs =
    [
        MediaForgeCapabilityCatalog.HardwareEncodeProof,
        MediaForgeCapabilityCatalog.RenderToEncodeProof,
        MediaForgeCapabilityCatalog.Mp4OutputProductProof
    ];

    private static readonly string[] RtmpProofs =
    [
        MediaForgeCapabilityCatalog.HardwareEncodeProof,
        MediaForgeCapabilityCatalog.RenderToEncodeProof,
        MediaForgeCapabilityCatalog.RtmpNetworkOutputProof
    ];

    private static readonly string[] VideoFileProofs =
    [
        MediaForgeCapabilityCatalog.HardwareDecodeProof,
        MediaForgeCapabilityCatalog.DecodeToRenderProof
    ];

    public CapabilityEntry ResolveRecordingCapability(HardwareMediaCapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ResolveCompositeCapability(
            report,
            CapabilityCategories.Sink,
            MediaForgeCapabilityCatalog.RecordingMp4H264,
            "Recording MP4 H.264",
            MediaForgeSupportStatus.Supported,
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaForgeLicenseStatus.Approved,
            RecordingMp4Proofs,
            "MP4 recording remains unavailable until hardware encode, render-to-encode, and MP4 product output proofs pass.",
            MediaTransportKind.EncodedPacket);
    }

    public CapabilityEntry ResolveStreamingCapability(HardwareMediaCapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ResolveCompositeCapability(
            report,
            CapabilityCategories.Sink,
            MediaForgeCapabilityCatalog.RtmpH264,
            "RTMP H.264 streaming",
            MediaForgeSupportStatus.Experimental,
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaForgeLicenseStatus.Approved,
            RtmpProofs,
            "RTMP streaming remains unavailable until hardware encode, render-to-encode, and RTMP network output proofs pass.",
            MediaTransportKind.EncodedPacket);
    }

    public CapabilityEntry ResolveVideoFileInputCapability(HardwareMediaCapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ResolveCompositeCapability(
            report,
            CapabilityCategories.Source,
            MediaForgeCapabilityCatalog.VideoFileMp4,
            "Video file MP4",
            MediaForgeSupportStatus.Experimental,
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaForgeLicenseStatus.Approved,
            VideoFileProofs,
            "MP4 video input remains unavailable until hardware decode and decode-to-render proofs pass.",
            MediaTransportKind.EncodedPacket);
    }

    public bool IsRecordingSupported(HardwareMediaCapabilityReport report) =>
        HasPassedProofs(report, RecordingMp4Proofs);

    public bool IsStreamingSupported(HardwareMediaCapabilityReport report) =>
        HasPassedProofs(report, RtmpProofs);

    public bool IsVideoFileInputSupported(HardwareMediaCapabilityReport report) =>
        HasPassedProofs(report, VideoFileProofs);

    public static bool HasPassedProofs(
        HardwareMediaCapabilityReport report,
        params string[] requiredProofIds)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(requiredProofIds);

        var proofs = CreateProofMap(report);
        return requiredProofIds.All(id =>
            proofs.TryGetValue(id, out var proof) &&
            proof.Status == HardwareMediaProofStatus.Passed);
    }

    private static CapabilityEntry ResolveCompositeCapability(
        HardwareMediaCapabilityReport report,
        string category,
        string id,
        string displayName,
        MediaForgeSupportStatus supportedStatus,
        MediaForgeProductReadinessStatus supportedReadiness,
        MediaForgeLicenseStatus licenseStatus,
        IReadOnlyList<string> requiredProofIds,
        string blockedReason,
        MediaTransportKind transportKind)
    {
        var proofs = CreateProofMap(report);
        var missing = requiredProofIds
            .Where(id => !proofs.TryGetValue(id, out var proof) || proof.Status != HardwareMediaProofStatus.Passed)
            .Select(id => FormatMissingProof(id, proofs))
            .ToArray();

        if (missing.Length == 0)
        {
            return new CapabilityEntry
            {
                Category = category,
                Id = id,
                DisplayName = displayName,
                SupportStatus = supportedStatus,
                LicenseStatus = licenseStatus,
                ProductReadinessStatus = supportedReadiness,
                TransportKind = transportKind,
                UnavailableReason = null
            };
        }

        return new CapabilityEntry
        {
            Category = category,
            Id = id,
            DisplayName = displayName,
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Prototype,
            TransportKind = transportKind,
            UnavailableReason = $"{blockedReason} Missing proof(s): {string.Join("; ", missing)}."
        };
    }

    private static IReadOnlyDictionary<string, HardwareMediaProof> CreateProofMap(
        HardwareMediaCapabilityReport report)
    {
        var proofs = new Dictionary<string, HardwareMediaProof>(StringComparer.OrdinalIgnoreCase);
        foreach (var proof in report.Proofs)
            proofs[proof.Id] = proof;

        return proofs;
    }

    private static string FormatMissingProof(
        string proofId,
        IReadOnlyDictionary<string, HardwareMediaProof> proofs)
    {
        if (!proofs.TryGetValue(proofId, out var proof))
            return $"{proofId}=Pending";

        return string.IsNullOrWhiteSpace(proof.Reason)
            ? $"{proofId}={proof.Status}"
            : $"{proofId}={proof.Status} ({proof.Reason})";
    }
}
