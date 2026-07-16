using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Graphics.Vulkan;
using WTK.MediaForge.Windows.Media;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Ndi;
using WTK.MediaForge.Windows.Media.Text;

namespace WTK.MediaForge.Windows;

public static class MediaForgeWindows
{
    private static readonly IHardwareMediaCapabilityProbe DefaultCapabilityProbe =
        new WindowsHardwareMediaCapabilityProbe();

    public static MediaForgeEngine CreateEngine(MediaForgeEngineOptions? options = null)
    {
        options ??= new MediaForgeEngineOptions();
        ValidateOptions(options);

        return new MediaForgeEngine(
            new CompositeMediaSourceProviderFactory(
            new WindowsDesktopSourceProviderFactory(options.Diagnostics),
            new WindowsImageSourceProviderFactory(options.Diagnostics),
            new WindowsWebcamSourceProviderFactory(options.Diagnostics),
            new WindowsNdiSourceProviderFactory(options.Diagnostics),
            new WindowsUnavailableLiveSourceProviderFactory(options.Diagnostics),
            new WindowsVideoFileSourceProviderFactory(options.Diagnostics)),
            new WindowsRenderOutputSinkFactory(),
            new MediaForgeVulkanRenderBackendFactory(new WindowsSystemDrawingFontAtlasRasterizer()),
            options.Diagnostics,
            new WindowsEncodedOutputRouteFactory(options.Diagnostics))
        {
            StartTimeout = options.StartTimeout,
            CommandTimeout = options.CommandTimeout,
            StopTimeout = options.StopTimeout,
            SinkStopTimeout = options.SinkStopTimeout,
            RenderFramesPerSecond = options.RenderFramesPerSecond,
            RenderThreadJoinTimeout = options.StopTimeout,
            RenderThreadSubmissionShutdownTimeout = options.StopTimeout
        };
    }

    public static ValueTask<MediaForgeCapabilityReport> GetCapabilityReportAsync(
        CancellationToken cancellationToken = default) =>
        GetCapabilityReportAsync(DefaultCapabilityProbe, cancellationToken);

    public static HardwareMediaProofRegistry CreateHardwareMediaProofRegistry()
    {
        var registry = new HardwareMediaProofRegistry();
        registry.Register(new WindowsRenderToH264EncodeProofRunner());
        registry.Register(new WindowsHardwareH264EncodeProofRunner());
        registry.Register(new WindowsMp4RecordingProofRunner());
        registry.Register(new WindowsMp4OutputProductProofRunner());
        registry.Register(new WindowsRtmpNetworkOutputProofRunner());
        registry.Register(new WindowsHardwareDecodeProofRunner());
        registry.Register(new WindowsDecodeToRenderProofRunner());
        registry.Register(new WindowsMp4InputProductProofRunner());
        registry.Register(new WindowsWebcamInputProductProofRunner());
        registry.Register(new WindowsNdiInputProductProofRunner());
        registry.Register(new WindowsNdiOutputProductProofRunner());
        return registry;
    }

    public static ValueTask<MediaForgeCapabilityReport> GetCapabilityReportWithHardwareProofsAsync(
        CancellationToken cancellationToken = default) =>
        GetCapabilityReportWithHardwareProofsAsync(DefaultCapabilityProbe, CreateHardwareMediaProofRegistry(), cancellationToken);

    public static async ValueTask<MediaForgeCapabilityReport> GetCapabilityReportAsync(
        IHardwareMediaCapabilityProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var hardware = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return MediaForgeCapabilityReportBuilder.Build(
            hardware,
            CreatePlatformCapabilityEntries(hardware));
    }

    public static async ValueTask<MediaForgeCapabilityReport> GetCapabilityReportWithHardwareProofsAsync(
        IHardwareMediaCapabilityProbe probe,
        HardwareMediaProofRegistry proofRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(proofRegistry);

        var hardware = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var proofResults = await proofRegistry.RunAsync(hardware, cancellationToken).ConfigureAwait(false);
        var mergedHardware = HardwareMediaProofRegistry.ApplyResults(hardware, proofResults);
        return MediaForgeCapabilityReportBuilder.Build(
            mergedHardware,
            CreatePlatformCapabilityEntries(mergedHardware));
    }

    private static IEnumerable<CapabilityEntry> CreatePlatformCapabilityEntries(
        HardwareMediaCapabilityReport hardware)
    {
        var ndiRuntime = new WindowsNdiRuntimeProbe().Probe();
        foreach (var entry in MediaSourceTypeRegistry.CreateCapabilityEntries())
        {
            if (entry.Id.Equals($"source.{MediaSourceTypes.Webcam.Value}", StringComparison.OrdinalIgnoreCase) &&
                HasPassedProof(hardware, MediaForgeCapabilityCatalog.WebcamInputProductProof))
            {
                yield return new CapabilityEntry
                {
                    Id = entry.Id,
                    Category = entry.Category,
                    DisplayName = entry.DisplayName,
                    SupportStatus = MediaForgeSupportStatus.Experimental,
                    LicenseStatus = entry.LicenseStatus,
                    ProductReadinessStatus = MediaForgeProductReadinessStatus.ProductValidated,
                    UnavailableReason = null,
                    TransportKind = entry.TransportKind
                };
                continue;
            }

            if (entry.Id.Equals($"source.{MediaSourceTypes.NdiInput.Value}", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNdiInputCapabilityEntry(entry, hardware, ndiRuntime);
                continue;
            }

            yield return entry;
        }

        foreach (var entry in RenderOutputTypeRegistry.CreateCapabilityEntries())
        {
            if (entry.Id.Equals($"output.{RenderOutputTypes.Ndi.Value}", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNdiOutputCapabilityEntry(entry, hardware, ndiRuntime);
                continue;
            }

            yield return entry;
        }
    }

    private static CapabilityEntry CreateNdiInputCapabilityEntry(
        CapabilityEntry entry,
        HardwareMediaCapabilityReport hardware,
        WindowsNdiRuntimeInfo runtime)
    {
        if (HasPassedProof(hardware, MediaForgeCapabilityCatalog.NdiInputProductProof))
        {
            return new CapabilityEntry
            {
                Id = entry.Id,
                Category = entry.Category,
                DisplayName = entry.DisplayName,
                SupportStatus = MediaForgeSupportStatus.Experimental,
                LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
                ProductReadinessStatus = MediaForgeProductReadinessStatus.ProductValidated,
                UnavailableReason = null,
                TransportKind = entry.TransportKind
            };
        }

        return new CapabilityEntry
        {
            Id = entry.Id,
            Category = entry.Category,
            DisplayName = entry.DisplayName,
            SupportStatus = runtime.CanUseStandardSdk
                ? MediaForgeSupportStatus.Blocked
                : MediaForgeSupportStatus.Unavailable,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = runtime.CanUseStandardSdk
                ? $"NDI runtime detected at '{runtime.LibraryPath}', but NDI input remains blocked until a GPU-safe source lease path is validated. Continuous raw CPU NDI frames are prohibited."
                : runtime.Reason,
            TransportKind = entry.TransportKind
        };
    }

    private static CapabilityEntry CreateNdiOutputCapabilityEntry(
        CapabilityEntry entry,
        HardwareMediaCapabilityReport hardware,
        WindowsNdiRuntimeInfo runtime)
    {
        if (HasPassedProof(hardware, MediaForgeCapabilityCatalog.NdiOutputProductProof))
        {
            return new CapabilityEntry
            {
                Id = entry.Id,
                Category = entry.Category,
                DisplayName = entry.DisplayName,
                SupportStatus = MediaForgeSupportStatus.Experimental,
                LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
                ProductReadinessStatus = MediaForgeProductReadinessStatus.ProductValidated,
                UnavailableReason = null,
                TransportKind = entry.TransportKind
            };
        }

        return new CapabilityEntry
        {
            Id = entry.Id,
            Category = entry.Category,
            DisplayName = entry.DisplayName,
            SupportStatus = runtime.CanUseStandardSdk
                ? MediaForgeSupportStatus.Blocked
                : MediaForgeSupportStatus.Unavailable,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = runtime.CanUseStandardSdk
                ? $"NDI runtime detected at '{runtime.LibraryPath}', but NDI output remains blocked until rendered GPU surfaces or hardware encoded packets can be sent without continuous CPU readback."
                : runtime.Reason,
            TransportKind = entry.TransportKind
        };
    }

    private static bool HasPassedProof(HardwareMediaCapabilityReport hardware, string proofId) =>
        hardware.Proofs.Any(proof =>
            proof.Id.Equals(proofId, StringComparison.OrdinalIgnoreCase) &&
            proof.Status == HardwareMediaProofStatus.Passed);

    private static void ValidateOptions(MediaForgeEngineOptions options)
    {
        if (options.StartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StartTimeout must be positive.");

        if (options.CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "CommandTimeout must be positive.");

        if (options.StopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StopTimeout must be positive.");

        if (options.SinkStopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "SinkStopTimeout must be positive.");

        if (!double.IsFinite(options.RenderFramesPerSecond) || options.RenderFramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RenderFramesPerSecond must be finite and positive.");
    }
}
