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
    public const string VideoFileMp4 = "source.video.file.mp4";
    public const string EnginePerformanceBaseline = "engine.performance.baseline";

    public static IReadOnlyList<CapabilityEntry> CreateDefaultEntries(GpuExportProofStatus exportProofStatus) =>
    [
        Entry(CapabilityCategories.Sink, RecordingMp4H264, "Recording MP4 H.264",
            MediaForgeSupportStatus.PrototypeOnly,
            MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Prototype,
            "Prototype only: current path does not yet prove real Media Foundation hardware encode or production MP4 muxing.",
            MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Sink, RtmpH264, "RTMP H.264 streaming",
            MediaForgeSupportStatus.PrototypeOnly, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Prototype,
            "Prototype only: current transport is in-memory and not a network RTMP implementation.",
            MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Sink, SrtOutput, "SRT streaming",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Blocked by license review and transport design."),

        Entry(CapabilityCategories.License, LibX264, "libx264",
            MediaForgeSupportStatus.Prohibited, MediaForgeLicenseStatus.Prohibited,
            MediaForgeProductReadinessStatus.Contract,
            "GPL encoder prohibited without commercial license."),

        Entry(CapabilityCategories.License, Ffmpeg, "FFmpeg integration",
            MediaForgeSupportStatus.NotUsedInMvp, MediaForgeLicenseStatus.NotUsedInMvp,
            MediaForgeProductReadinessStatus.Contract,
            "Not used in first hardware MP4/RTMP MVP."),

        Entry(CapabilityCategories.Encode, MfHardwareH264, "Media Foundation hardware MFT H.264",
            MediaForgeSupportStatus.PrototypeOnly, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Prototype,
            "Prototype only until real hardware MFT enumeration and backend output validation are implemented."),

        Entry(CapabilityCategories.Source, VideoFileMp4, "Video file MP4",
            MediaForgeSupportStatus.PrototypeOnly, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Prototype,
            "Prototype only: current Windows decode bridge does not decode actual media content into a GPU surface.",
            MediaTransportKind.EncodedPacket),

        Entry(CapabilityCategories.Encode, NvencDirect, "NVENC direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF MVP; vendor SDK license review required."),

        Entry(CapabilityCategories.Encode, QsvDirect, "Intel QSV direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF MVP; vendor SDK license review required."),

        Entry(CapabilityCategories.Encode, AmfDirect, "AMD AMF direct",
            MediaForgeSupportStatus.Planned, MediaForgeLicenseStatus.RequiresLegalReview,
            MediaForgeProductReadinessStatus.Contract,
            "Post-MF MVP; vendor SDK license review required."),

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
                _ => "Awaiting Commit 06 export proof."
            },
            MediaTransportKind.GpuSurface),

        Entry(CapabilityCategories.Performance, EnginePerformanceBaseline, "Engine performance baseline",
            MediaForgeSupportStatus.PrototypeOnly,
            MediaForgeLicenseStatus.Approved,
            MediaForgeProductReadinessStatus.Skeleton,
            "Synthetic performance validation exists, but real render/decode/encode workloads are not product-validated.",
            MediaTransportKind.GpuSurface)
    ];

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
}
