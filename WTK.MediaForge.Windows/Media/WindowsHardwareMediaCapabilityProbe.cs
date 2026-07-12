using System.Runtime.InteropServices;
using Vortice.DXGI;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media;

public sealed class WindowsHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apis = new List<string> { "D3D11", "D3D11VA", "Vulkan", "MediaFoundation" };
        var (vendor, deviceName) = TryGetPrimaryAdapterInfo();
        var report = new HardwareMediaCapabilityReport
        {
            Platform = RuntimeInformation.OSDescription,
            GpuVendor = vendor,
            DeviceName = deviceName,
            DetectedApis = apis,
            HardwareDecodeCodecs = [],
            HardwareEncodeCodecs = [],
            AcceptsGpuSurfaceInput = false,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "Real Media Foundation hardware encode/decode output validation has not completed; prototype bridges are excluded.",
            BackendCapabilities = CreateBackendCapabilities(vendor),
            Proofs = CreateProofs(vendor)
        };

        return ValueTask.FromResult(report);
    }

    private static IReadOnlyList<HardwareMediaBackendCapability> CreateBackendCapabilities(string? vendor) =>
    [
        new HardwareMediaBackendCapability
        {
            Id = "windows.mf.d3d11va.decode.h264",
            DisplayName = "Windows Media Foundation D3D11VA H.264 decode",
            Platform = "Windows",
            Vendor = vendor,
            DecodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Prototype,
            UnavailableReason = "Real D3D11VA output surface validation is not complete; placeholder decode bridges are excluded."
        },
        new HardwareMediaBackendCapability
        {
            Id = "windows.mf.hardware_mft.encode.h264",
            DisplayName = "Windows Media Foundation hardware MFT H.264 encode",
            Platform = "Windows",
            Vendor = vendor,
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Prototype,
            UnavailableReason = "Real hardware MFT packet validation is not complete; canned packet bridges are excluded."
        },
        new HardwareMediaBackendCapability
        {
            Id = "linux.vaapi.drm.decode_encode",
            DisplayName = "Linux VAAPI/DRM GPU media",
            Platform = "Linux",
            DecodeCodecs = ["H264"],
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Planned,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Linux VAAPI/DRM adapters must live in a Linux-specific project and are not implemented yet."
        },
        new HardwareMediaBackendCapability
        {
            Id = "linux.vulkan_video.decode_encode",
            DisplayName = "Linux Vulkan Video GPU media",
            Platform = "Linux",
            DecodeCodecs = ["H264"],
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Planned,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Vulkan Video adapters are planned and require runtime capability validation."
        },
        new HardwareMediaBackendCapability
        {
            Id = "macos.videotoolbox.decode_encode",
            DisplayName = "macOS VideoToolbox GPU media",
            Platform = "macOS",
            DecodeCodecs = ["H264"],
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Planned,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "VideoToolbox/CVPixelBuffer/IOSurface bridge must live in a macOS-specific project and is not implemented yet."
        }
    ];

    private static IReadOnlyList<HardwareMediaProof> CreateProofs(string? vendor) =>
    [
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.RenderToEncodeProof,
            DisplayName = "Rendered output to hardware encoder input proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "Vulkan-D3D11-MediaFoundation",
            Vendor = vendor,
            Reason = "Render-to-encode proof must be executed by the readiness gate on a hardware validation machine."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.HardwareEncodeProof,
            DisplayName = "Hardware H.264 encode proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-HardwareMft",
            Vendor = vendor,
            Reason = "Hardware encode proof has not been validated for this runtime capability report."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.Mp4RecordingProof,
            DisplayName = "MP4 recording proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-HardwareMft+NativeMp4Mux",
            Vendor = vendor,
            Reason = "MP4 recording remains unavailable until hardware encode and production mux proof both pass."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.HardwareDecodeProof,
            DisplayName = "Hardware H.264 decode proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-D3D11VA",
            Vendor = vendor,
            Reason = "Hardware decode proof has not been validated for this runtime capability report."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.DecodeToRenderProof,
            DisplayName = "Hardware decode to renderer proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-D3D11VA+Vulkan",
            Vendor = vendor,
            Reason = "Decode-to-render proof remains unavailable until D3D11VA produces a validated GPU surface submitted to the renderer."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.Mp4OutputProductProof,
            DisplayName = "MP4 output product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "Vulkan-D3D11-MediaFoundation+NativeMp4Mux",
            Vendor = vendor,
            Reason = "MP4 output remains unavailable until render-to-encode, hardware encode, and packet-only MP4 mux proofs pass together."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.Mp4InputProductProof,
            DisplayName = "MP4 input product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-D3D11VA+Vulkan",
            Vendor = vendor,
            Reason = "MP4 input remains unavailable until real file demux, hardware decode, decoded GPU frame adaptation, and renderer submission are validated."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.WebcamInputProductProof,
            DisplayName = "Webcam input product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-Webcam-D3D11Upload",
            Vendor = vendor,
            Reason = "Webcam input remains unavailable until system frames are uploaded immediately to GPU with bounded KeepLatest backpressure and product validation."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.RtmpNetworkOutputProof,
            DisplayName = "RTMP network output proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NativeRtmpTransport",
            Vendor = vendor,
            Reason = "TCP RTMP transport exists; proof remains unavailable until hardware-encoded packets from the render-output encode pipeline publish without blocking the render thread."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.NdiInputProductProof,
            DisplayName = "NDI input product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NDI-SDK",
            Vendor = vendor,
            Reason = "NDI input is unsupported until SDK licensing is approved and a GPU-safe input path is validated."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.NdiOutputProductProof,
            DisplayName = "NDI output product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NDI-SDK",
            Vendor = vendor,
            Reason = "NDI output is unsupported until SDK licensing is approved and output avoids continuous CPU readback."
        }
    ];

    private static (string Vendor, string DeviceName) TryGetPrimaryAdapterInfo()
    {
        if (!OperatingSystem.IsWindows())
            return ("Unknown", "Unknown");

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            using (adapter)
            {
                var description = adapter.Description1;
                return (MapVendor(description.VendorId), description.Description);
            }
        }
        catch
        {
            return ("Unknown", "Unknown");
        }
    }

    private static string MapVendor(uint vendorId) =>
        vendorId switch
        {
            0x10DE => "NVIDIA",
            0x1002 or 0x1022 => "AMD/Radeon",
            0x8086 => "Intel",
            _ => $"Unknown(0x{vendorId:X4})"
        };
}
