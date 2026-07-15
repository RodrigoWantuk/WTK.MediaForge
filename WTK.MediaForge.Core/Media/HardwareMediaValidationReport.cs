using System.Text;

namespace WTK.MediaForge.Core.Media;

public sealed class HardwareMediaValidationReport
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required bool RequireHardwareMedia { get; init; }

    public required HardwareMediaValidationStatus OverallStatus { get; init; }

    public required bool ReleaseGatePassed { get; init; }

    public required string Platform { get; init; }

    public string? GpuVendor { get; init; }

    public string? DeviceName { get; init; }

    public string? DriverVersion { get; init; }

    public IReadOnlyList<string> DetectedApis { get; init; } = Array.Empty<string>();

    public IReadOnlyList<HardwareMediaValidationCapability> Capabilities { get; init; } =
        Array.Empty<HardwareMediaValidationCapability>();

    public IReadOnlyList<HardwareMediaValidationProof> Proofs { get; init; } =
        Array.Empty<HardwareMediaValidationProof>();

    public IReadOnlyList<HardwareMediaValidationFeature> Features { get; init; } =
        Array.Empty<HardwareMediaValidationFeature>();

    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
}

public sealed class HardwareMediaValidationCapability
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Category { get; init; }

    public required MediaForgeSupportStatus SupportStatus { get; init; }

    public required MediaForgeProductReadinessStatus ProductReadinessStatus { get; init; }

    public MediaTransportKind? TransportKind { get; init; }

    public string? Reason { get; init; }
}

public sealed class HardwareMediaValidationProof
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required HardwareMediaValidationStatus Status { get; init; }

    public string? Backend { get; init; }

    public string? Vendor { get; init; }

    public required string Reason { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class HardwareMediaValidationFeature
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? CapabilityId { get; init; }

    public required HardwareMediaValidationStatus Status { get; init; }

    public required bool RequiredForHardwareRelease { get; init; }

    public IReadOnlyList<string> RequiredProofIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MissingProofIds { get; init; } = Array.Empty<string>();

    public required string Reason { get; init; }
}

public enum HardwareMediaValidationStatus
{
    Passed,
    Planned,
    Unavailable,
    Blocked,
    Failed,
    NotImplemented
}

public static class HardwareMediaValidationReportBuilder
{
    private static readonly FeatureSpec[] FeatureSpecs =
    [
        new(
            "feature.recording.mp4.h264",
            "MP4 recording product path",
            MediaForgeCapabilityCatalog.RecordingMp4H264,
            [
                MediaForgeCapabilityCatalog.HardwareEncodeProof,
                MediaForgeCapabilityCatalog.RenderToEncodeProof,
                MediaForgeCapabilityCatalog.Mp4RecordingProof,
                MediaForgeCapabilityCatalog.Mp4OutputProductProof
            ],
            RequiredForHardwareRelease: true),
        new(
            "feature.streaming.rtmp.h264",
            "RTMP H.264 streaming product path",
            MediaForgeCapabilityCatalog.RtmpH264,
            [
                MediaForgeCapabilityCatalog.HardwareEncodeProof,
                MediaForgeCapabilityCatalog.RenderToEncodeProof,
                MediaForgeCapabilityCatalog.RtmpNetworkOutputProof
            ],
            RequiredForHardwareRelease: true),
        new(
            "feature.input.mp4.h264",
            "MP4 video input product path",
            MediaForgeCapabilityCatalog.VideoFileMp4,
            [
                MediaForgeCapabilityCatalog.HardwareDecodeProof,
                MediaForgeCapabilityCatalog.DecodeToRenderProof,
                MediaForgeCapabilityCatalog.Mp4InputProductProof
            ],
            RequiredForHardwareRelease: true),
        new(
            "feature.input.webcam",
            "Webcam input product path",
            "source.wtk.source.webcam",
            [MediaForgeCapabilityCatalog.WebcamInputProductProof],
            RequiredForHardwareRelease: true),
        new(
            "feature.capture.desktop",
            "Desktop capture product path",
            "source.wtk.source.desktop",
            [],
            RequiredForHardwareRelease: false),
        new(
            "feature.capture.window",
            "Window capture product path",
            "source.wtk.source.window.capture",
            [],
            RequiredForHardwareRelease: false),
        new(
            "feature.input.ndi",
            "NDI input product path",
            "source.wtk.source.ndi.input",
            [MediaForgeCapabilityCatalog.NdiInputProductProof],
            RequiredForHardwareRelease: true),
        new(
            "feature.output.ndi",
            "NDI output product path",
            "output.wtk.output.ndi",
            [MediaForgeCapabilityCatalog.NdiOutputProductProof],
            RequiredForHardwareRelease: true)
    ];

    private static readonly (string Id, string DisplayName)[] KnownProofs =
    [
        (MediaForgeCapabilityCatalog.RenderToEncodeProof, "Rendered output to hardware encoder input proof"),
        (MediaForgeCapabilityCatalog.HardwareEncodeProof, "Hardware H.264 encode proof"),
        (MediaForgeCapabilityCatalog.Mp4RecordingProof, "MP4 recording proof"),
        (MediaForgeCapabilityCatalog.HardwareDecodeProof, "Hardware H.264 decode proof"),
        (MediaForgeCapabilityCatalog.DecodeToRenderProof, "Hardware decode to renderer proof"),
        (MediaForgeCapabilityCatalog.Mp4OutputProductProof, "MP4 output product proof"),
        (MediaForgeCapabilityCatalog.Mp4InputProductProof, "MP4 input product proof"),
        (MediaForgeCapabilityCatalog.WebcamInputProductProof, "Webcam input product proof"),
        (MediaForgeCapabilityCatalog.RtmpNetworkOutputProof, "RTMP network output proof"),
        (MediaForgeCapabilityCatalog.NdiInputProductProof, "NDI input product proof"),
        (MediaForgeCapabilityCatalog.NdiOutputProductProof, "NDI output product proof")
    ];

    public static HardwareMediaValidationReport Build(
        MediaForgeCapabilityReport capabilityReport,
        bool requireHardwareMedia,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);

        var proofMap = CreateProofMap(capabilityReport.Hardware);
        var capabilityMap = capabilityReport.Entries
            .ToDictionary(static entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        var proofs = KnownProofs
            .Select(proof => CreateValidationProof(proof.Id, proof.DisplayName, proofMap))
            .ToArray();

        var features = FeatureSpecs
            .Select(spec => CreateFeature(spec, capabilityMap, proofMap))
            .ToArray();

        EnsureReasons(proofs, features);

        var releaseFailures = requireHardwareMedia
            ? features
                .Where(static feature => feature.RequiredForHardwareRelease)
                .Where(static feature => feature.Status != HardwareMediaValidationStatus.Passed)
                .Select(static feature => $"{feature.DisplayName}: {feature.Reason}")
                .ToArray()
            : [];

        var failures = proofs
            .Where(static proof => proof.Status == HardwareMediaValidationStatus.Failed)
            .Select(static proof => $"{proof.DisplayName}: {proof.Reason}")
            .Concat(releaseFailures)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var overallStatus = ResolveOverallStatus(features, proofs, requireHardwareMedia);

        return new HardwareMediaValidationReport
        {
            GeneratedAtUtc = generatedAtUtc ?? DateTimeOffset.UtcNow,
            RequireHardwareMedia = requireHardwareMedia,
            OverallStatus = overallStatus,
            ReleaseGatePassed = failures.Length == 0,
            Platform = capabilityReport.Hardware.Platform,
            GpuVendor = capabilityReport.Hardware.GpuVendor,
            DeviceName = capabilityReport.Hardware.DeviceName,
            DriverVersion = capabilityReport.Hardware.DriverVersion,
            DetectedApis = capabilityReport.Hardware.DetectedApis,
            Capabilities = capabilityReport.Entries
                .OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => new HardwareMediaValidationCapability
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Category = entry.Category,
                    SupportStatus = entry.SupportStatus,
                    ProductReadinessStatus = entry.ProductReadinessStatus,
                    TransportKind = entry.TransportKind,
                    Reason = entry.UnavailableReason
                })
                .ToArray(),
            Proofs = proofs,
            Features = features,
            Failures = failures
        };
    }

    private static Dictionary<string, HardwareMediaProof> CreateProofMap(
        HardwareMediaCapabilityReport hardware)
    {
        var proofs = new Dictionary<string, HardwareMediaProof>(StringComparer.OrdinalIgnoreCase);
        foreach (var proof in hardware.Proofs)
            proofs[proof.Id] = proof;

        foreach (var (id, displayName) in KnownProofs)
        {
            proofs.TryAdd(id, new HardwareMediaProof
            {
                Id = id,
                DisplayName = displayName,
                Status = HardwareMediaProofStatus.Pending,
                Reason = "Proof has not been executed."
            });
        }

        return proofs;
    }

    private static HardwareMediaValidationProof CreateValidationProof(
        string id,
        string displayName,
        IReadOnlyDictionary<string, HardwareMediaProof> proofMap)
    {
        var proof = proofMap[id];
        return new HardwareMediaValidationProof
        {
            Id = proof.Id,
            DisplayName = string.IsNullOrWhiteSpace(proof.DisplayName) ? displayName : proof.DisplayName,
            Status = MapProofStatus(proof.Status),
            Backend = proof.Backend,
            Vendor = proof.Vendor,
            Reason = BuildProofReason(proof),
            Evidence = proof.Evidence
        };
    }

    private static HardwareMediaValidationFeature CreateFeature(
        FeatureSpec spec,
        IReadOnlyDictionary<string, CapabilityEntry> capabilityMap,
        IReadOnlyDictionary<string, HardwareMediaProof> proofMap)
    {
        capabilityMap.TryGetValue(spec.CapabilityId, out var capability);

        var missingProofs = spec.RequiredProofIds
            .Where(id => !proofMap.TryGetValue(id, out var proof) || proof.Status != HardwareMediaProofStatus.Passed)
            .ToArray();

        if (missingProofs.Length == 0 && IsCapabilityAvailable(capability))
        {
            return new HardwareMediaValidationFeature
            {
                Id = spec.Id,
                DisplayName = spec.DisplayName,
                CapabilityId = spec.CapabilityId,
                Status = HardwareMediaValidationStatus.Passed,
                RequiredForHardwareRelease = spec.RequiredForHardwareRelease,
                RequiredProofIds = spec.RequiredProofIds,
                MissingProofIds = [],
                Reason = "Required capability and proof chain are available."
            };
        }

        var status = missingProofs.Length > 0
            ? ResolveMissingProofFeatureStatus(missingProofs, proofMap)
            : MapCapabilityStatus(capability);

        var reason = BuildFeatureReason(spec, capability, missingProofs, proofMap);

        return new HardwareMediaValidationFeature
        {
            Id = spec.Id,
            DisplayName = spec.DisplayName,
            CapabilityId = spec.CapabilityId,
            Status = status,
            RequiredForHardwareRelease = spec.RequiredForHardwareRelease,
            RequiredProofIds = spec.RequiredProofIds,
            MissingProofIds = missingProofs,
            Reason = reason
        };
    }

    private static bool IsCapabilityAvailable(CapabilityEntry? capability) =>
        capability is not null &&
        capability.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental &&
        capability.ProductReadinessStatus is not MediaForgeProductReadinessStatus.Prototype and
            not MediaForgeProductReadinessStatus.Skeleton;

    private static HardwareMediaValidationStatus ResolveMissingProofFeatureStatus(
        IReadOnlyList<string> missingProofIds,
        IReadOnlyDictionary<string, HardwareMediaProof> proofMap)
    {
        if (missingProofIds.Any(id => proofMap[id].Status == HardwareMediaProofStatus.Failed))
            return HardwareMediaValidationStatus.Failed;

        return HardwareMediaValidationStatus.Blocked;
    }

    private static HardwareMediaValidationStatus MapCapabilityStatus(CapabilityEntry? capability)
    {
        if (capability is null)
            return HardwareMediaValidationStatus.NotImplemented;

        return capability.SupportStatus switch
        {
            MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental =>
                HardwareMediaValidationStatus.Passed,
            MediaForgeSupportStatus.Planned or MediaForgeSupportStatus.Deferred =>
                HardwareMediaValidationStatus.Planned,
            MediaForgeSupportStatus.Unsupported or MediaForgeSupportStatus.Unavailable =>
                HardwareMediaValidationStatus.Unavailable,
            MediaForgeSupportStatus.Blocked or MediaForgeSupportStatus.PrototypeOnly =>
                HardwareMediaValidationStatus.Blocked,
            MediaForgeSupportStatus.InternalOnly =>
                HardwareMediaValidationStatus.Blocked,
            _ => HardwareMediaValidationStatus.Unavailable
        };
    }

    private static HardwareMediaValidationStatus MapProofStatus(HardwareMediaProofStatus status) =>
        status switch
        {
            HardwareMediaProofStatus.Passed => HardwareMediaValidationStatus.Passed,
            HardwareMediaProofStatus.Failed => HardwareMediaValidationStatus.Failed,
            HardwareMediaProofStatus.Unavailable => HardwareMediaValidationStatus.Unavailable,
            HardwareMediaProofStatus.Skipped => HardwareMediaValidationStatus.Planned,
            _ => HardwareMediaValidationStatus.NotImplemented
        };

    private static string BuildFeatureReason(
        FeatureSpec spec,
        CapabilityEntry? capability,
        IReadOnlyList<string> missingProofs,
        IReadOnlyDictionary<string, HardwareMediaProof> proofMap)
    {
        var parts = new List<string>();

        if (capability is null)
        {
            parts.Add($"Capability '{spec.CapabilityId}' is not present in the capability report.");
        }
        else if (!IsCapabilityAvailable(capability))
        {
            parts.Add(string.IsNullOrWhiteSpace(capability.UnavailableReason)
                ? $"Capability '{capability.Id}' is {capability.SupportStatus}/{capability.ProductReadinessStatus}."
                : capability.UnavailableReason!);
        }

        var capabilityReasonAlreadyListsProofs =
            capability?.UnavailableReason?.Contains("Missing proof(s):", StringComparison.OrdinalIgnoreCase) == true;

        if (missingProofs.Count > 0 && !capabilityReasonAlreadyListsProofs)
        {
            parts.Add("Missing proof(s): " + string.Join("; ", missingProofs.Select(id =>
            {
                var proof = proofMap[id];
                return string.IsNullOrWhiteSpace(proof.Reason)
                    ? $"{id}={proof.Status}"
                    : $"{id}={proof.Status} ({proof.Reason})";
            })));
        }

        return parts.Count == 0
            ? "Required capability and proof chain are available."
            : string.Join(" ", parts);
    }

    private static string BuildProofReason(HardwareMediaProof proof)
    {
        if (proof.Status == HardwareMediaProofStatus.Passed)
            return "Proof passed.";

        if (string.IsNullOrWhiteSpace(proof.Reason))
        {
            throw new InvalidOperationException(
                $"Hardware media proof '{proof.Id}' is marked {proof.Status} but does not provide a reason.");
        }

        return proof.Reason!;
    }

    private static void EnsureReasons(
        IReadOnlyList<HardwareMediaValidationProof> proofs,
        IReadOnlyList<HardwareMediaValidationFeature> features)
    {
        foreach (var proof in proofs)
        {
            if (proof.Status != HardwareMediaValidationStatus.Passed &&
                string.IsNullOrWhiteSpace(proof.Reason))
            {
                throw new InvalidOperationException(
                    $"Validation proof '{proof.Id}' is {proof.Status} but does not provide a reason.");
            }
        }

        foreach (var feature in features)
        {
            if (feature.Status != HardwareMediaValidationStatus.Passed &&
                string.IsNullOrWhiteSpace(feature.Reason))
            {
                throw new InvalidOperationException(
                    $"Validation feature '{feature.Id}' is {feature.Status} but does not provide a reason.");
            }
        }
    }

    private static HardwareMediaValidationStatus ResolveOverallStatus(
        IReadOnlyList<HardwareMediaValidationFeature> features,
        IReadOnlyList<HardwareMediaValidationProof> proofs,
        bool requireHardwareMedia)
    {
        if (proofs.Any(static proof => proof.Status == HardwareMediaValidationStatus.Failed))
            return HardwareMediaValidationStatus.Failed;

        if (requireHardwareMedia &&
            features
                .Where(static feature => feature.RequiredForHardwareRelease)
                .Any(static feature => feature.Status != HardwareMediaValidationStatus.Passed))
        {
            return HardwareMediaValidationStatus.Failed;
        }

        if (features.Any(static feature => feature.Status == HardwareMediaValidationStatus.Blocked))
            return HardwareMediaValidationStatus.Blocked;

        if (features.Any(static feature => feature.Status is
                HardwareMediaValidationStatus.Unavailable or
                HardwareMediaValidationStatus.Planned or
                HardwareMediaValidationStatus.NotImplemented))
        {
            return HardwareMediaValidationStatus.Unavailable;
        }

        return HardwareMediaValidationStatus.Passed;
    }

    private sealed record FeatureSpec(
        string Id,
        string DisplayName,
        string CapabilityId,
        IReadOnlyList<string> RequiredProofIds,
        bool RequiredForHardwareRelease);
}

public static class HardwareMediaValidationReportMarkdownWriter
{
    public static string Write(HardwareMediaValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("# WTK MediaForge Media Proof Report");
        builder.AppendLine();
        builder.AppendLine($"- Schema: {report.SchemaVersion}");
        builder.AppendLine($"- Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"- Platform: {report.Platform}");
        builder.AppendLine($"- GPU vendor: {Empty(report.GpuVendor)}");
        builder.AppendLine($"- Device: {Empty(report.DeviceName)}");
        builder.AppendLine($"- Driver: {Empty(report.DriverVersion)}");
        builder.AppendLine($"- Require hardware media: {report.RequireHardwareMedia}");
        builder.AppendLine($"- Overall status: {report.OverallStatus}");
        builder.AppendLine($"- Release gate passed: {report.ReleaseGatePassed}");
        builder.AppendLine();

        builder.AppendLine("## Features");
        builder.AppendLine();
        builder.AppendLine("| Feature | Status | Required proofs | Missing proofs | Reason |");
        builder.AppendLine("|---|---:|---|---|---|");
        foreach (var feature in report.Features)
        {
            builder.AppendLine(
                $"| {Escape(feature.DisplayName)} | {feature.Status} | {FormatList(feature.RequiredProofIds)} | {FormatList(feature.MissingProofIds)} | {Escape(feature.Reason)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Proofs");
        builder.AppendLine();
        builder.AppendLine("| Proof | Status | Backend | Evidence | Reason |");
        builder.AppendLine("|---|---:|---|---|---|");
        foreach (var proof in report.Proofs)
        {
            builder.AppendLine(
                $"| {Escape(proof.DisplayName)} | {proof.Status} | {Escape(Empty(proof.Backend))} | {FormatList(proof.Evidence)} | {Escape(proof.Reason)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Release Failures");
        builder.AppendLine();
        if (report.Failures.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var failure in report.Failures)
                builder.AppendLine($"- {failure}");
        }

        return builder.ToString();
    }

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0
            ? "None"
            : Escape(string.Join("<br>", values));

    private static string Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value!;

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
