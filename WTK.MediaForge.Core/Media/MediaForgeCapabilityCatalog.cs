namespace WTK.MediaForge.Core.Media;

public static class MediaForgeCapabilityCatalog
{
    public const string RecordingMp4H264 = "output.recording.mp4.h264";
    public const string RtmpH264 = "output.streaming.rtmp.h264";
    public const string SrtOutput = "output.streaming.srt";
    public const string LibX264 = "encoder.libx264";
    public const string Ffmpeg = "integration.ffmpeg";
    public const string NvencDirect = "encoder.nvenc.direct";
    public const string QsvDirect = "encoder.qsv.direct";
    public const string AmfDirect = "encoder.amf.direct";
    public const string MfHardwareH264 = "encoder.mf.hardware.h264";
    public const string GpuExportProof = "interop.gpu.export.proof";
    public const string RenderToEncodeProof = "proof.render_to_encode.gpu";
    public const string HardwareEncodeProof = "proof.hardware_encode.h264";
    public const string Mp4RecordingProof = "proof.recording.mp4.h264";
    public const string HardwareDecodeProof = "proof.hardware_decode.h264";
    public const string DecodeToRenderProof = "proof.decode_to_render.gpu";
    public const string Mp4OutputProductProof = "proof.media_io.mp4_output.product";
    public const string Mp4InputProductProof = "proof.media_io.mp4_input.product";
    public const string WebcamInputProductProof = "proof.media_io.webcam_input.product";
    public const string WindowCaptureInputProductProof = "proof.media_io.window_capture.product";
    public const string RtmpNetworkOutputProof = "proof.media_io.rtmp_output.network";
    public const string NdiSourceDiscovery = "source.ndi.discovery";
    public const string NdiInputProductProof = "proof.media_io.ndi_input.product";
    public const string NdiOutputProductProof = "proof.media_io.ndi_output.product";
    public const string VideoFileMp4 = "source.video.file.mp4";
    public const string EnginePerformanceBaseline = "engine.performance.baseline";
    public const string RemoteScenePublish = "remote-scene.publish";
    public const string RemoteSceneSubscribe = "remote-scene.subscribe";
    public const string RemoteSceneDirectProof = "proof.remote_scene.direct";
    public const string RemoteSceneTurnProof = "proof.remote_scene.turn";

    public static IReadOnlyList<CapabilityEntry> CreateDefaultEntries(GpuExportProofStatus exportProofStatus) =>
        CreateDefaultEntries(new HardwareMediaCapabilityReport
        {
            Platform = "Unknown",
            ExportProofStatus = exportProofStatus
        });

    public static IReadOnlyList<CapabilityEntry> CreateDefaultEntries(HardwareMediaCapabilityReport hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        var exportProofStatus = hardware.ExportProofStatus;
        var proofs = CreateProofMap(hardware);
        var proofAggregator = new CapabilityProofAggregator();
        return
    [
        proofAggregator.ResolveRecordingCapability(hardware),

        proofAggregator.ResolveStreamingCapability(hardware),

        Entry(CapabilityCategories.Sink, SrtOutput, "SRT streaming",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Blocked by license review and transport design."),

        Entry(CapabilityCategories.Sink, RemoteScenePublish, "Remote Scene publish",
            MediaForgeSupportStatus.Unavailable, MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Contract,
            "Unavailable until the pinned libwebrtc backend and Direct/TURN hardware encode proofs pass.",
            MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Source, RemoteSceneSubscribe, "Remote Scene subscribe",
            MediaForgeSupportStatus.Unavailable, MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Contract,
            "Unavailable until the pinned libwebrtc backend, Direct/TURN receive, hardware decode, and decode-to-render proofs pass.",
            MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Proof, RemoteSceneDirectProof, "Remote Scene direct path proof",
            MediaForgeSupportStatus.Unavailable, MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Contract,
            "Direct proof has not passed on two physical peers.", MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Proof, RemoteSceneTurnProof, "Remote Scene TURN path proof",
            MediaForgeSupportStatus.Unavailable, MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Contract,
            "TURN proof has not passed on two physical peers.", MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.License, LibX264, "libx264",
            MediaForgeSupportStatus.Prohibited, MediaForgeLicenseStatus.Prohibited,
            MediaForgeProductReadinessStatus.Contract,
            "GPL encoder prohibited without commercial license."),

        Entry(CapabilityCategories.License, Ffmpeg, "FFmpeg integration",
            MediaForgeSupportStatus.Deferred, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Deferred until the native hardware media product path is sustained and a separate encoded-packet/container-only legal review approves the scope."),

        Entry(CapabilityCategories.Encode, MfHardwareH264, "Media Foundation hardware MFT H.264",
            GetProof(proofs, HardwareEncodeProof).Status == HardwareMediaProofStatus.Passed
                ? MediaForgeSupportStatus.Supported
                : MediaForgeSupportStatus.Unavailable,
            MediaForgeLicenseStatus.Approved,
            GetProof(proofs, HardwareEncodeProof).Status == HardwareMediaProofStatus.Passed
                ? MediaForgeProductReadinessStatus.BackendCallSucceeded
                : MediaForgeProductReadinessStatus.Contract,
            GetProof(proofs, HardwareEncodeProof).Status == HardwareMediaProofStatus.Passed
                ? "Media Foundation hardware H.264 encode proof passed on this runtime."
                : "Unavailable until the hardware H.264 encode proof passes on this runtime."),

        proofAggregator.ResolveVideoFileInputCapability(hardware),

        Entry(CapabilityCategories.Encode, NvencDirect, "NVENC direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF hardware product path; vendor SDK license review required."),

        Entry(CapabilityCategories.Encode, QsvDirect, "Intel QSV direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF hardware product path; vendor SDK license review required."),

        Entry(CapabilityCategories.Encode, AmfDirect, "AMD AMF direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF hardware product path; vendor SDK license review required."),

        Entry(CapabilityCategories.ExportProof, GpuExportProof, "Vulkan to encoder GPU surface export",
            exportProofStatus switch
            {
                GpuExportProofStatus.Passed => MediaForgeSupportStatus.Supported,
                GpuExportProofStatus.Failed => MediaForgeSupportStatus.Blocked,
                _ => MediaForgeSupportStatus.Planned
            },
            MediaForgeLicenseStatus.Approved,
            exportProofStatus == GpuExportProofStatus.Passed
                ? MediaForgeProductReadinessStatus.BackendCallSucceeded
                : MediaForgeProductReadinessStatus.Contract,
            exportProofStatus switch
            {
                GpuExportProofStatus.Passed => "GPU export proof passed.",
                GpuExportProofStatus.Failed => "GPU export proof failed; recording blocked.",
                _ => "GPU export proof has not passed on this runtime."
            },
            MediaTransportKind.GpuSurface),

        ProofEntry(
            RenderToEncodeProof,
            "Rendered output to hardware encoder input proof",
            GetProof(proofs, RenderToEncodeProof),
            MediaForgeProductReadinessStatus.BackendCallSucceeded),

        ProofEntry(
            HardwareEncodeProof,
            "Hardware H.264 encode proof",
            GetProof(proofs, HardwareEncodeProof),
            MediaForgeProductReadinessStatus.BackendCallSucceeded,
            MediaTransportKind.EncodedPacket),

        ProofEntry(
            Mp4RecordingProof,
            "MP4 recording proof",
            GetProof(proofs, Mp4RecordingProof),
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaTransportKind.EncodedPacket),

        ProofEntry(
            HardwareDecodeProof,
            "Hardware H.264 decode proof",
            GetProof(proofs, HardwareDecodeProof),
            MediaForgeProductReadinessStatus.BackendCallSucceeded),

        ProofEntry(
            DecodeToRenderProof,
            "Hardware decode to renderer proof",
            GetProof(proofs, DecodeToRenderProof),
            MediaForgeProductReadinessStatus.BackendCallSucceeded),

        ProofEntry(
            Mp4OutputProductProof,
            "MP4 output product proof",
            GetProof(proofs, Mp4OutputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaTransportKind.EncodedPacket),

        ProofEntry(
            Mp4InputProductProof,
            "MP4 input product proof",
            GetProof(proofs, Mp4InputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated),

        ProofEntry(
            WebcamInputProductProof,
            "Webcam input product proof",
            GetProof(proofs, WebcamInputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated),

        ProofEntry(
            WindowCaptureInputProductProof,
            "Window capture input product proof",
            GetProof(proofs, WindowCaptureInputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated),

        ProofEntry(
            RtmpNetworkOutputProof,
            "RTMP network output proof",
            GetProof(proofs, RtmpNetworkOutputProof),
            MediaForgeProductReadinessStatus.ProductValidated,
            MediaTransportKind.EncodedPacket),

        ProofEntry(
            NdiInputProductProof,
            "NDI input product proof",
            GetProof(proofs, NdiInputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated),

        ProofEntry(
            NdiOutputProductProof,
            "NDI output product proof",
            GetProof(proofs, NdiOutputProductProof),
            MediaForgeProductReadinessStatus.ProductValidated),

        Entry(CapabilityCategories.Performance, EnginePerformanceBaseline, "Engine performance baseline",
            MediaForgeSupportStatus.Deferred,
            MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Contract,
            "Short real composition and Vulkan workloads pass, but product promotion requires the documented 30-minute qualification and eight-hour release-candidate workloads.",
            MediaTransportKind.GpuSurface)
    ];
    }

    private static CapabilityEntry Entry(
        string category,
        string id,
        string displayName,
        MediaForgeSupportStatus support,
        MediaForgeLicenseStatus license,
        MediaForgeProductReadinessStatus readiness,
        string? reason = null,
        MediaTransportKind? transport = null) =>
        new()
        {
            Category = category,
            Id = id,
            DisplayName = displayName,
            SupportStatus = support,
            LicenseStatus = license,
            ProductReadinessStatus = readiness,
            UnavailableReason = reason,
            TransportKind = transport
        };

    private static CapabilityEntry ProofEntry(
        string id,
        string displayName,
        HardwareMediaProof proof,
        MediaForgeProductReadinessStatus passedReadiness,
        MediaTransportKind transport = MediaTransportKind.GpuSurface) =>
        Entry(
            CapabilityCategories.Proof,
            id,
            displayName,
            MapProofSupportStatus(proof.Status),
            MediaForgeLicenseStatus.Approved,
            proof.Status == HardwareMediaProofStatus.Passed
                ? passedReadiness
                : MediaForgeProductReadinessStatus.Contract,
            BuildProofReason(proof),
            transport);

    private static IReadOnlyDictionary<string, HardwareMediaProof> CreateProofMap(
        HardwareMediaCapabilityReport hardware)
    {
        var map = new Dictionary<string, HardwareMediaProof>(StringComparer.OrdinalIgnoreCase);
        foreach (var proof in hardware.Proofs)
            map[proof.Id] = proof;

        map.TryAdd(
            RenderToEncodeProof,
            PendingProof(RenderToEncodeProof, "Rendered output to hardware encoder input proof"));
        map.TryAdd(
            HardwareEncodeProof,
            PendingProof(HardwareEncodeProof, "Hardware H.264 encode proof"));
        map.TryAdd(
            Mp4RecordingProof,
            PendingProof(Mp4RecordingProof, "MP4 recording proof"));
        map.TryAdd(
            HardwareDecodeProof,
            PendingProof(HardwareDecodeProof, "Hardware H.264 decode proof"));
        map.TryAdd(
            DecodeToRenderProof,
            PendingProof(DecodeToRenderProof, "Hardware decode to renderer proof"));
        map.TryAdd(
            Mp4OutputProductProof,
            PendingProof(Mp4OutputProductProof, "MP4 output product proof"));
        map.TryAdd(
            Mp4InputProductProof,
            PendingProof(Mp4InputProductProof, "MP4 input product proof"));
        map.TryAdd(
            WebcamInputProductProof,
            PendingProof(WebcamInputProductProof, "Webcam input product proof"));
        map.TryAdd(
            WindowCaptureInputProductProof,
            PendingProof(WindowCaptureInputProductProof, "Window capture input product proof"));
        map.TryAdd(
            RtmpNetworkOutputProof,
            PendingProof(RtmpNetworkOutputProof, "RTMP network output proof"));
        map.TryAdd(
            NdiInputProductProof,
            PendingProof(NdiInputProductProof, "NDI input product proof"));
        map.TryAdd(
            NdiOutputProductProof,
            PendingProof(NdiOutputProductProof, "NDI output product proof"));

        return map;
    }

    private static HardwareMediaProof PendingProof(string id, string displayName) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Status = HardwareMediaProofStatus.Pending,
            Reason = "Proof has not been executed."
        };

    private static HardwareMediaProof GetProof(
        IReadOnlyDictionary<string, HardwareMediaProof> proofs,
        string id) =>
        proofs.TryGetValue(id, out var proof)
            ? proof
            : PendingProof(id, id);

    private static MediaForgeSupportStatus MapProofSupportStatus(HardwareMediaProofStatus status) =>
        status switch
        {
            HardwareMediaProofStatus.Passed => MediaForgeSupportStatus.Supported,
            HardwareMediaProofStatus.Failed => MediaForgeSupportStatus.Blocked,
            HardwareMediaProofStatus.Unavailable => MediaForgeSupportStatus.Unavailable,
            HardwareMediaProofStatus.Skipped => MediaForgeSupportStatus.Planned,
            _ => MediaForgeSupportStatus.Planned
        };

    private static string BuildProofReason(HardwareMediaProof proof) =>
        proof.Status == HardwareMediaProofStatus.Passed
            ? $"Proof passed{FormatBackend(proof)}."
            : string.IsNullOrWhiteSpace(proof.Reason)
                ? $"Proof is {proof.Status}."
                : proof.Reason!;

    private static string FormatBackend(HardwareMediaProof proof)
    {
        if (string.IsNullOrWhiteSpace(proof.Backend) && string.IsNullOrWhiteSpace(proof.Vendor))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(proof.Vendor))
            return $" on {proof.Backend}";

        if (string.IsNullOrWhiteSpace(proof.Backend))
            return $" on {proof.Vendor}";

        return $" on {proof.Backend}/{proof.Vendor}";
    }
}
