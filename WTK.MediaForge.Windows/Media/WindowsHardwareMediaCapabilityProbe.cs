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
            BackendCapabilities = CreateBackendCapabilities(vendor)
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
