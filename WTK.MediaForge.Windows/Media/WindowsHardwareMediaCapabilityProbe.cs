using System.Runtime.InteropServices;
using Vortice.DXGI;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media.Ndi;

namespace WTK.MediaForge.Windows.Media;

public sealed class WindowsHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ndiRuntime = new WindowsNdiRuntimeProbe().Probe();
        var apis = new List<string> { "D3D11", "D3D11VA", "Vulkan", "MediaFoundation" };
        if (ndiRuntime.CanUseStandardSdk)
            apis.Add("NDI-SDK");

        var (vendor, deviceName, adapterId) = TryGetPrimaryAdapterInfo();
        var report = new HardwareMediaCapabilityReport
        {
            Platform = RuntimeInformation.OSDescription,
            GpuVendor = vendor,
            DeviceName = deviceName,
            AdapterId = adapterId,
            DetectedApis = apis,
            HardwareDecodeCodecs = [],
            HardwareEncodeCodecs = [],
            AcceptsGpuSurfaceInput = false,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "Run the v14 proof runners to validate hardware encode/decode on this machine; baseline probing does not promote product media without executed proof evidence.",
            BackendCapabilities = CreateBackendCapabilities(vendor, ndiRuntime),
            Proofs = CreateProofs(vendor, ndiRuntime)
        };

        return ValueTask.FromResult(report);
    }

    private static IReadOnlyList<HardwareMediaBackendCapability> CreateBackendCapabilities(
        string? vendor,
        WindowsNdiRuntimeInfo ndiRuntime) =>
    [
        new HardwareMediaBackendCapability
        {
            Id = "windows.mf.d3d11va.decode.h264",
            DisplayName = "Windows Media Foundation D3D11VA H.264 decode",
            Platform = "Windows",
            Vendor = vendor,
            DecodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Unavailable,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Run the v14 hardware decode proof to validate real D3D11VA IMFDXGIBuffer output for this machine."
        },
        new HardwareMediaBackendCapability
        {
            Id = "windows.mf.hardware_mft.encode.h264",
            DisplayName = "Windows Media Foundation hardware MFT H.264 encode",
            Platform = "Windows",
            Vendor = vendor,
            EncodeCodecs = ["H264"],
            SupportStatus = MediaForgeSupportStatus.Unavailable,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Run the v14 hardware encode proof to validate real Media Foundation hardware MFT packets for this machine."
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
        },
        new HardwareMediaBackendCapability
        {
            Id = "windows.ndi.standard.runtime",
            DisplayName = "Windows NDI SDK runtime",
            Platform = "Windows",
            Vendor = "Vizrt NDI AB",
            DecodeCodecs = ["NDI"],
            EncodeCodecs = ["NDI"],
            RequiresGpuSurface = true,
            RequiresCpuStaging = !ndiRuntime.HasProductSafeGpuPath,
            SupportStatus = ndiRuntime.CanUseStandardSdk
                ? MediaForgeSupportStatus.Blocked
                : MediaForgeSupportStatus.Unavailable,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = ndiRuntime.CanUseStandardSdk
                ? $"NDI Standard SDK runtime is installed and loadable at '{ndiRuntime.LibraryPath}'. Discovery is allowed, but MediaForge requires a GPU-safe NDI video path before product input/output support."
                : ndiRuntime.Reason
        }
    ];

    private static IReadOnlyList<HardwareMediaProof> CreateProofs(
        string? vendor,
        WindowsNdiRuntimeInfo ndiRuntime) =>
    [
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.RenderToEncodeProof,
            DisplayName = "Rendered output to hardware encoder input proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "Vulkan-D3D11-MediaFoundation",
            Vendor = vendor,
            Reason = "Render-to-encode proof must execute the Vulkan offscreen render, D3D11 export/conversion, and hardware encoder path on this machine."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.HardwareEncodeProof,
            DisplayName = "Hardware H.264 encode proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-HardwareMft",
            Vendor = vendor,
            Reason = "Hardware encode proof must execute Media Foundation hardware MFT output validation for this runtime capability report."
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
            Reason = "Hardware decode proof must execute Media Foundation D3D11VA and validate IMFDXGIBuffer output for this runtime capability report."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.DecodeToRenderProof,
            DisplayName = "Hardware decode to renderer proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "MediaFoundation-D3D11VA+Vulkan",
            Vendor = vendor,
            Reason = "Decode-to-render proof must execute D3D11VA decode, source lease adaptation, Vulkan import, and an offscreen render submission."
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
            Id = MediaForgeCapabilityCatalog.WindowCaptureInputProductProof,
            DisplayName = "Window capture input product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "WindowsGraphicsCapture-D3D11+Vulkan",
            Vendor = vendor,
            Reason = "Window capture remains unavailable until a real HWND is captured into a D3D11 GPU lease and rendered through Vulkan."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.RtmpNetworkOutputProof,
            DisplayName = "RTMP network output proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NativeRtmpTransport",
            Vendor = vendor,
            Reason = "RTMP network proof must execute local TCP RTMP handshake/publish using hardware-encoded packets from the render-output encode pipeline."
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.NdiInputProductProof,
            DisplayName = "NDI input product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NDI-SDK",
            Vendor = vendor,
            Reason = ndiRuntime.CanUseStandardSdk
                ? $"NDI Standard SDK runtime detected at '{ndiRuntime.LibraryPath}', but video input remains blocked until a GPU-safe source lease path is validated."
                : ndiRuntime.Reason
        },
        new HardwareMediaProof
        {
            Id = MediaForgeCapabilityCatalog.NdiOutputProductProof,
            DisplayName = "NDI output product proof",
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = "NDI-SDK",
            Vendor = vendor,
            Reason = ndiRuntime.CanUseStandardSdk
                ? $"NDI Standard SDK runtime detected at '{ndiRuntime.LibraryPath}', but video output remains blocked until a GPU-safe send path is validated."
                : ndiRuntime.Reason
        }
    ];

    private static (string Vendor, string DeviceName, string AdapterId) TryGetPrimaryAdapterInfo()
    {
        if (!OperatingSystem.IsWindows())
            return ("Unknown", "Unknown", "unavailable");

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            using (adapter)
            {
                var description = adapter.Description1;
                var luid = description.Luid;
                return (
                    MapVendor(description.VendorId),
                    description.Description,
                    $"{luid.HighPart:X8}:{luid.LowPart:X8}");
            }
        }
        catch
        {
            return ("Unknown", "Unknown", "unavailable");
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
