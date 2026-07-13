using System.Reflection;
using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan;
using WTK.MediaForge.Windows;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public class PublicApiSurfaceTests
{
    [Fact]
    public void Public_api_matches_approved_allowlist()
    {
        var assemblies = new[]
        {
            typeof(FrameSize).Assembly,
            typeof(MediaForgeDiagnostic).Assembly,
            typeof(MediaForgeProject).Assembly,
            typeof(DesktopMonitorEnumerator).Assembly,
            typeof(D3D11GpuDevice).Assembly,
            typeof(VulkanRendererInfo).Assembly,
            typeof(MediaForgeWindows).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name!;
            var exported = assembly
                .GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                ApprovedPublicTypes.TryGetValue(assemblyName, out var approved),
                $"No public API allowlist exists for assembly '{assemblyName}'.");

            var missing = approved.Except(exported, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var extra = exported.Except(approved, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Assert.True(
                missing.Length == 0 && extra.Length == 0,
                $"Public API mismatch for '{assemblyName}'.{Environment.NewLine}" +
                $"Missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}{Environment.NewLine}" +
                $"Extra:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
        }
    }

    [Fact]
    public void Public_api_does_not_expose_internal_runtime_types()
    {
        var composition = typeof(MediaForgeProject).Assembly;
        var core = typeof(FrameSize).Assembly;
        var capture = typeof(DesktopMonitorEnumerator).Assembly;
        var vulkan = typeof(VulkanRendererInfo).Assembly;

        var prohibited = new (Assembly Assembly, string TypeName)[]
        {
            (composition, "WTK.MediaForge.Composition.Runtime.CompositionRuntime"),
            (composition, "WTK.MediaForge.Composition.Runtime.LatestSnapshotBuffer"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.MediaForgeRenderThread"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.PendingRenderSubmissionTracker"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.RenderThreadGuard"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.IRenderBackend"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.IRenderBackendFactory"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.IRenderFrameSubmission"),
            (composition, "WTK.MediaForge.Composition.Runtime.Rendering.RenderOutputBindingSnapshot"),
            (composition, "WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink"),
            (composition, "WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSinkFactory"),
            (composition, "WTK.MediaForge.Composition.Runtime.Sources.IMediaSourceProviderFactory"),
            (composition, "WTK.MediaForge.Composition.Snapshots.ProjectStateSnapshot"),
            (composition, "WTK.MediaForge.Composition.Snapshots.ProjectStateSnapshotFactory"),
            (composition, "WTK.MediaForge.Composition.Snapshots.RenderFrameSnapshot"),
            (composition, "WTK.MediaForge.Composition.Snapshots.RenderFrameSnapshotFactory"),
            (composition, "WTK.MediaForge.Composition.Snapshots.SnapshotBuildResult"),
            (composition, "WTK.MediaForge.Composition.Shaders.ShaderPipelineCatalog"),
            (composition, "WTK.MediaForge.Composition.Shaders.RenderDrawObjectPipelineMapper"),
            (core, "WTK.MediaForge.Core.Gpu.GpuFrameLease"),
            (core, "WTK.MediaForge.Core.Sources.IMediaSource"),
            (core, "WTK.MediaForge.Core.Sources.IVideoFrameProvider"),
            (capture, "WTK.MediaForge.Capture.DesktopDuplication.DesktopDuplicationFrameProvider"),
            (capture, "WTK.MediaForge.Capture.Gpu.D3D11GpuFrameSlot"),
            (capture, "WTK.MediaForge.Capture.Gpu.D3D11GpuFrameSlotRing"),
            (vulkan, "WTK.MediaForge.Graphics.Vulkan.MediaForgeVulkanRenderBackendFactory"),
            (vulkan, "WTK.MediaForge.Graphics.Vulkan.VulkanSmokeTest"),
            (vulkan, "WTK.MediaForge.Graphics.Vulkan.Rendering.MediaForgeVulkanRenderer"),
            (vulkan, "WTK.MediaForge.Graphics.Vulkan.Rendering.VulkanExternalTextureRegistry"),
            (vulkan, "WTK.MediaForge.Graphics.Vulkan.Rendering.VulkanD3D11TextureImport")
        };

        foreach (var (assembly, typeName) in prohibited)
        {
            var type = assembly.GetType(typeName, throwOnError: true)!;

            Assert.DoesNotContain(type, assembly.GetExportedTypes());
            Assert.False(type.IsPublic, $"{type.FullName} must not be public.");
            Assert.False(type.IsNestedPublic, $"{type.FullName} must not be nested public.");
        }
    }

    private static readonly IReadOnlyDictionary<string, string[]> ApprovedPublicTypes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["WTK.MediaForge.Core"] =
            [
                "WTK.MediaForge.Core.Capture.CaptureCenterPixel",
                "WTK.MediaForge.Core.Capture.CaptureDuplicationSizes",
                "WTK.MediaForge.Core.Capture.CaptureFrameStats",
                "WTK.MediaForge.Core.Capture.CapturePreviewGeometry",
                "WTK.MediaForge.Core.Capture.CaptureSessionInfo",
                "WTK.MediaForge.Core.Capture.CaptureSourceInfo",
                "WTK.MediaForge.Core.Capture.DesktopRect",
                "WTK.MediaForge.Core.Capture.DisplayRotation",
                "WTK.MediaForge.Core.Capture.GpuAdapterLuid",
                "WTK.MediaForge.Core.Color.ColorRgba",
                "WTK.MediaForge.Core.CoordinateSystem",
                "WTK.MediaForge.Core.Frames.FrameSize",
                "WTK.MediaForge.Core.Geometry.CanvasPoint",
                "WTK.MediaForge.Core.Geometry.CanvasRect",
                "WTK.MediaForge.Core.Geometry.CanvasSize",
                "WTK.MediaForge.Core.Geometry.NormalizedPoint",
                "WTK.MediaForge.Core.Geometry.NormalizedRect",
                "WTK.MediaForge.Core.Geometry.Transform2D",
                "WTK.MediaForge.Core.Gpu.GpuFrameBackend",
                "WTK.MediaForge.Core.Gpu.GpuFrameReference",
                "WTK.MediaForge.Core.Gpu.GpuTextureId",
                "WTK.MediaForge.Core.Gpu.IGpuFrameHandle",
                "WTK.MediaForge.Core.Gpu.IRetiredGpuResource",
                "WTK.MediaForge.Core.Gpu.Resources.GpuTextureLease",
                "WTK.MediaForge.Core.Gpu.RetiredGpuResourceFailure",
                "WTK.MediaForge.Core.Gpu.RetiredGpuResourceManager",
                "WTK.MediaForge.Core.Gpu.Slots.FakeGpuFrameSlotHandle",
                "WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotLease",
                "WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotRing",
                "WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotState",
                "WTK.MediaForge.Core.Identifiers.CanvasId",
                "WTK.MediaForge.Core.Identifiers.DrawObjectId",
                "WTK.MediaForge.Core.Identifiers.EffectId",
                "WTK.MediaForge.Core.Identifiers.MediaSourceTypeId",
                "WTK.MediaForge.Core.Identifiers.RenderOutputId",
                "WTK.MediaForge.Core.Identifiers.RenderOutputTypeId",
                "WTK.MediaForge.Core.Identifiers.SourceId",
                "WTK.MediaForge.Core.Media.Audit.CollectingMediaTransportAuditSink",
                "WTK.MediaForge.Core.Media.Audit.IMediaTransportAuditSink",
                "WTK.MediaForge.Core.Media.Audit.MediaTransportAuditEvent",
                "WTK.MediaForge.Core.Media.Audit.MediaTransportAuditEvidenceKind",
                "WTK.MediaForge.Core.Media.Audit.MediaTransportAuditEventKind",
                "WTK.MediaForge.Core.Media.Audit.MediaTransportAuditRules",
                "WTK.MediaForge.Core.Media.BlendMode",
                "WTK.MediaForge.Core.Media.CapabilityCategories",
                "WTK.MediaForge.Core.Media.CapabilityEntry",
                "WTK.MediaForge.Core.Media.CapabilityProofAggregator",
                "WTK.MediaForge.Core.Media.ContentFitLayout",
                "WTK.MediaForge.Core.Media.ContentFitRect",
                "WTK.MediaForge.Core.Media.Decode.DecodeFrameContext",
                "WTK.MediaForge.Core.Media.Decode.DecodedGpuFrame",
                "WTK.MediaForge.Core.Media.Decode.FileDecodeFrameContext",
                "WTK.MediaForge.Core.Media.Decode.HardwareDecodeOpenContext",
                "WTK.MediaForge.Core.Media.Decode.HardwareDecodeSession",
                "WTK.MediaForge.Core.Media.Decode.HardwareDecoderInfo",
                "WTK.MediaForge.Core.Media.Decode.IHardwareFileVideoDecoder",
                "WTK.MediaForge.Core.Media.Decode.IHardwareVideoDecoder",
                "WTK.MediaForge.Core.Media.Encode.EncodeFrameContext",
                "WTK.MediaForge.Core.Media.Encode.GpuTextureLeaseExtensions",
                "WTK.MediaForge.Core.Media.Encode.H264NalUtilities",
                "WTK.MediaForge.Core.Media.Encode.HardwareEncodeFrameContext",
                "WTK.MediaForge.Core.Media.Encode.HardwareEncoderInfo",
                "WTK.MediaForge.Core.Media.Encode.HardwareVideoEncoderSettings",
                "WTK.MediaForge.Core.Media.Encode.IHardwareVideoEncoder",
                "WTK.MediaForge.Core.Media.EncodedVideoBitstreamFormat",
                "WTK.MediaForge.Core.Media.EncodedVideoCodec",
                "WTK.MediaForge.Core.Media.EncodedVideoPacket",
                "WTK.MediaForge.Core.Media.EncodedVideoPacketEvidence",
                "WTK.MediaForge.Core.Media.EncodedVideoPacketLease",
                "WTK.MediaForge.Core.Media.FrameRate",
                "WTK.MediaForge.Core.Media.GpuExportProofStatus",
                "WTK.MediaForge.Core.Media.GpuVideoFrameDescriptor",
                "WTK.MediaForge.Core.Media.GpuVideoFrameLease",
                "WTK.MediaForge.Core.Media.HardwareMediaBackendCapability",
                "WTK.MediaForge.Core.Media.HardwareMediaProof",
                "WTK.MediaForge.Core.Media.HardwareMediaProofRegistry",
                "WTK.MediaForge.Core.Media.HardwareMediaProofResult",
                "WTK.MediaForge.Core.Media.HardwareMediaProofRunner",
                "WTK.MediaForge.Core.Media.HardwareMediaProofStatus",
                "WTK.MediaForge.Core.Media.HardwareMediaCapabilityReport",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationCapability",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationFeature",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationProof",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationReport",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationReportBuilder",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationReportMarkdownWriter",
                "WTK.MediaForge.Core.Media.HardwareMediaValidationStatus",
                "WTK.MediaForge.Core.Media.IHardwareMediaCapabilityProbe",
                "WTK.MediaForge.Core.Media.Interop.GpuExternalFrameDescriptor",
                "WTK.MediaForge.Core.Media.Interop.HardwareEncoderInputLease",
                "WTK.MediaForge.Core.Media.Interop.HardwareEncoderInputRequirement",
                "WTK.MediaForge.Core.Media.Interop.IHardwareEncoderFormatConverter",
                "WTK.MediaForge.Core.Media.Interop.IGpuFrameExporter",
                "WTK.MediaForge.Core.Media.Interop.IGpuFrameImporter",
                "WTK.MediaForge.Core.Media.LayoutMode",
                "WTK.MediaForge.Core.Media.MediaForgeCapabilityCatalog",
                "WTK.MediaForge.Core.Media.MediaForgeCapabilityReport",
                "WTK.MediaForge.Core.Media.MediaForgeCapabilityReportBuilder",
                "WTK.MediaForge.Core.Media.MediaForgeLicenseStatus",
                "WTK.MediaForge.Core.Media.MediaForgeProductReadinessStatus",
                "WTK.MediaForge.Core.Media.MediaForgeSupportStatus",
                "WTK.MediaForge.Core.Media.MediaTransportKind",
                "WTK.MediaForge.Core.Media.NullHardwareMediaCapabilityProbe",
                "WTK.MediaForge.Core.Media.RawCpuVideoFrameException",
                "WTK.MediaForge.Core.Media.RawCpuVideoFrameExceptionAttribute",
                "WTK.MediaForge.Core.Media.RawCpuVideoFrameExceptionKind",
                "WTK.MediaForge.Core.Time.IVideoClock",
                "WTK.MediaForge.Core.Time.MediaTime"
            ],
            ["WTK.MediaForge.Diagnostics"] =
            [
                "WTK.MediaForge.Diagnostics.IMediaForgeDiagnosticsSink",
                "WTK.MediaForge.Diagnostics.InMemoryDiagnosticsSink",
                "WTK.MediaForge.Diagnostics.ListDiagnosticsSink",
                "WTK.MediaForge.Diagnostics.MediaForgeDiagnostic",
                "WTK.MediaForge.Diagnostics.MediaForgeDiagnosticFactory",
                "WTK.MediaForge.Diagnostics.MediaForgeDiagnosticSeverity",
                "WTK.MediaForge.Diagnostics.MediaForgeDiagnostics",
                "WTK.MediaForge.Diagnostics.MediaForgeMediaTelemetry",
                "WTK.MediaForge.Diagnostics.MediaForgeMediaTelemetrySnapshot",
                "WTK.MediaForge.Diagnostics.NullDiagnosticsSink"
            ],
            ["WTK.MediaForge.Composition"] =
            [
                "WTK.MediaForge.Composition.DrawObjects.CanvasDrawObject",
                "WTK.MediaForge.Composition.DrawObjects.MediaForgeDrawObject",
                "WTK.MediaForge.Composition.DrawObjects.SolidDrawObject",
                "WTK.MediaForge.Composition.DrawObjects.SourceLayerDrawObject",
                "WTK.MediaForge.Composition.DrawObjects.TextDrawObject",
                "WTK.MediaForge.Composition.Assets.AssetManager",
                "WTK.MediaForge.Composition.Editor.MediaForgeProjectEditor",
                "WTK.MediaForge.Composition.Effects.BlurEffect",
                "WTK.MediaForge.Composition.Effects.ChromaKeyEffect",
                "WTK.MediaForge.Composition.Effects.ColorCorrectionEffect",
                "WTK.MediaForge.Composition.Effects.MediaForgeEffect",
                "WTK.MediaForge.Composition.Effects.TransitionEffect",
                "WTK.MediaForge.Composition.Effects.TransitionKind",
                "WTK.MediaForge.Composition.Engine.MediaForgeDiagnosticEventArgs",
                "WTK.MediaForge.Composition.Engine.MediaForgeEngine",
                "WTK.MediaForge.Composition.Engine.MediaForgeEngineException",
                "WTK.MediaForge.Composition.Engine.MediaForgeEngineState",
                "WTK.MediaForge.Composition.Engine.MediaForgeEngineStateChangedEventArgs",
                "WTK.MediaForge.Composition.Engine.MediaForgeFrameDroppedEventArgs",
                "WTK.MediaForge.Composition.MediaForgeUnsupportedFeatureException",
                "WTK.MediaForge.Composition.Media.Stream.FlvPacket",
                "WTK.MediaForge.Composition.Media.Stream.FlvPacketizer",
                "WTK.MediaForge.Composition.Outputs.CpuReadbackFrame",
                "WTK.MediaForge.Composition.Outputs.CpuReadbackFrameEventArgs",
                "WTK.MediaForge.Composition.Outputs.CpuReadbackSink",
                "WTK.MediaForge.Composition.Outputs.EncodedOutputRuntimeSnapshot",
                "WTK.MediaForge.Composition.Outputs.EncodedOutputRuntimeStatus",
                "WTK.MediaForge.Composition.Outputs.EncodedPacketSinkContext",
                "WTK.MediaForge.Composition.Outputs.FrameNotificationEventArgs",
                "WTK.MediaForge.Composition.Outputs.FrameNotificationSink",
                "WTK.MediaForge.Composition.Outputs.IEncodedPacketSink",
                "WTK.MediaForge.Composition.Outputs.IRenderOutputSettings",
                "WTK.MediaForge.Composition.Outputs.IRenderOutputSink",
                "WTK.MediaForge.Composition.Outputs.MediaForgeOutputs",
                "WTK.MediaForge.Composition.Outputs.OffscreenRenderOutputTarget",
                "WTK.MediaForge.Composition.Outputs.OutputRouteTransition",
                "WTK.MediaForge.Composition.Outputs.OutputRouteTransitionKind",
                "WTK.MediaForge.Composition.Outputs.OutputRouteTransitionRuntime",
                "WTK.MediaForge.Composition.Outputs.PreviewPanelSink",
                "WTK.MediaForge.Composition.Outputs.RecordingMp4PacketSink",
                "WTK.MediaForge.Composition.Outputs.RecordingMp4Sink",
                "WTK.MediaForge.Composition.Outputs.RenderBackendKind",
                "WTK.MediaForge.Composition.Outputs.RenderOutputFrameInfo",
                "WTK.MediaForge.Composition.Outputs.RenderOutputFrameLease",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSettingsSerializer",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkBackpressureMode",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkContext",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkComplianceEntry",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkComplianceRegistry",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkId",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkKind",
                "WTK.MediaForge.Composition.Outputs.RenderOutputSinkTransport",
                "WTK.MediaForge.Composition.Outputs.RenderOutputTarget",
                "WTK.MediaForge.Composition.Outputs.RenderOutputTypeDescriptor",
                "WTK.MediaForge.Composition.Outputs.RenderOutputTypeRegistry",
                "WTK.MediaForge.Composition.Outputs.RenderOutputTypes",
                "WTK.MediaForge.Composition.Outputs.RenderPixelFormat",
                "WTK.MediaForge.Composition.Outputs.RtmpSink",
                "WTK.MediaForge.Composition.Outputs.RtmpPacketSink",
                "WTK.MediaForge.Composition.Outputs.Settings.EncodedFileOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.NdiOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.OffscreenOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.PreviewWindowOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.RecordingMp4OutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.StreamingHlsOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.StreamingRtspOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.StreamingRtmpOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.StreamingSrtOutputSettings",
                "WTK.MediaForge.Composition.Outputs.Settings.VirtualCameraOutputSettings",
                "WTK.MediaForge.Composition.Outputs.WinFormsPreviewRenderOutputTarget",
                "WTK.MediaForge.Composition.Project.CanvasLayerBuilder",
                "WTK.MediaForge.Composition.Project.MediaForgeCanvas",
                "WTK.MediaForge.Composition.Project.MediaForgeProject",
                "WTK.MediaForge.Composition.Project.MediaForgeProjectBuilder",
                "WTK.MediaForge.Composition.Project.MediaForgeProjectLoader",
                "WTK.MediaForge.Composition.Project.MediaForgeProjectMigrator",
                "WTK.MediaForge.Composition.Project.MediaForgeRenderOutput",
                "WTK.MediaForge.Composition.Project.MediaForgeSourceDefinition",
                "WTK.MediaForge.Composition.Project.ProjectLoadResult",
                "WTK.MediaForge.Composition.Project.ProjectMigrateResult",
                "WTK.MediaForge.Composition.Project.SourceLayerBuilder",
                "WTK.MediaForge.Composition.Project.TextLayerBuilder",
                "WTK.MediaForge.Composition.Project.MediaForgeProjectSerializer",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeCanvasPreset",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeEffectPreset",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeOutputPreset",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgePackageExportOptions",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgePackageSerializer",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeProjectImportMode",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeProjectImportResult",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeProjectPackages",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeScenePackage",
                "WTK.MediaForge.Composition.Project.Packages.MediaForgeSourcePreset",
                "WTK.MediaForge.Composition.Runtime.Recovery.FaultRecoveryCoordinator",
                "WTK.MediaForge.Composition.Runtime.Recovery.FaultRecoveryScenario",
                "WTK.MediaForge.Composition.Runtime.Recovery.FaultRecoveryState",
                "WTK.MediaForge.Composition.Runtime.RenderFrameContext",
                "WTK.MediaForge.Composition.Runtime.Streaming.ITextureStreamConsumer",
                "WTK.MediaForge.Composition.Runtime.Streaming.TextureLeaseQueue",
                "WTK.MediaForge.Composition.Runtime.Streaming.TextureLeaseQueuePolicy",
                "WTK.MediaForge.Composition.Serialization.MediaForgeProjectJsonOptions",
                "WTK.MediaForge.Composition.Sources.IMediaSourceSettings",
                "WTK.MediaForge.Composition.Sources.MediaForgeSources",
                "WTK.MediaForge.Composition.Sources.MediaSourceCategory",
                "WTK.MediaForge.Composition.Sources.MediaSourceSettingsSerializer",
                "WTK.MediaForge.Composition.Sources.MediaSourceTypeDescriptor",
                "WTK.MediaForge.Composition.Sources.MediaSourceTypeRegistry",
                "WTK.MediaForge.Composition.Sources.MediaSourceTypes",
                "WTK.MediaForge.Composition.Sources.Settings.AnimatedImageSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.DesktopCaptureSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.GeneratedSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.ImageFileSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.IpCameraSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.LottieSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.NdiInputSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.RtspInputSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.RtspTransportMode",
                "WTK.MediaForge.Composition.Sources.Settings.VideoFileSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.WebcamSourceSettings",
                "WTK.MediaForge.Composition.Sources.Settings.WindowCaptureSourceSettings",
                "WTK.MediaForge.Composition.Sources.IStaticImageAssetDecoder",
                "WTK.MediaForge.Composition.Sources.StaticCpuAsset",
                "WTK.MediaForge.Composition.Sources.StaticImageAssetFormats",
                "WTK.MediaForge.Composition.Sources.WebcamRawInputAdapter",
                "WTK.MediaForge.Composition.Validation.CanvasGraphLimits",
                "WTK.MediaForge.Composition.Validation.IRenderOutputDefinitionValidator",
                "WTK.MediaForge.Composition.Validation.ISourceDefinitionValidator",
                "WTK.MediaForge.Composition.Validation.MediaForgeProjectValidationException",
                "WTK.MediaForge.Composition.Validation.MediaForgeProjectValidator",
                "WTK.MediaForge.Composition.Validation.ProjectValidationResult",
                "WTK.MediaForge.Composition.Validation.ProjectValidationResultExtensions",
                "WTK.MediaForge.Composition.Validation.RenderOutputDefinitionValidatorRegistry",
                "WTK.MediaForge.Composition.Validation.SourceDefinitionValidatorRegistry",
                "WTK.MediaForge.Composition.Validation.ValidationIssue",
                "WTK.MediaForge.Composition.Validation.ValidationSeverity"
            ],
            ["WTK.MediaForge.Capture"] =
            [
                "WTK.MediaForge.Capture.DesktopDuplication.DesktopMonitorEnumerator"
            ],
            ["WTK.MediaForge.Graphics.D3D11"] =
            [
                "WTK.MediaForge.Graphics.D3D11.D3D11GpuDevice",
                "WTK.MediaForge.Graphics.D3D11.D3D11SharedTextureFactory",
                "WTK.MediaForge.Graphics.D3D11.D3D11SharedTextureFrameHandle",
                "WTK.MediaForge.Graphics.D3D11.D3D11SharedTextureSyncKeys",
                "WTK.MediaForge.Graphics.D3D11.SharedWin32Handle",
                "WTK.MediaForge.Graphics.D3D11.Win32NativeHandle"
            ],
            ["WTK.MediaForge.Graphics.Vulkan"] =
            [
                "WTK.MediaForge.Graphics.Vulkan.VulkanRendererInfo"
            ],
            ["WTK.MediaForge.Windows"] =
            [
                "WTK.MediaForge.Windows.Media.Decode.MediaFoundationHardwareVideoDecoder",
                "WTK.MediaForge.Windows.Media.Decode.WindowsHardwareVideoDecoderProbe",
                "WTK.MediaForge.Windows.Media.Encode.MediaFoundationHardwareEncoderProbe",
                "WTK.MediaForge.Windows.Media.Encode.MediaFoundationHardwareVideoEncoder",
                "WTK.MediaForge.Windows.Media.Interop.VulkanToD3D11EncoderSurfaceExporter",
                "WTK.MediaForge.Windows.Media.Interop.WindowsGpuExportProofDiagnostics",
                "WTK.MediaForge.Windows.Media.WindowsHardwareMediaCapabilityProbe",
                "WTK.MediaForge.Windows.MediaForgeEngineOptions",
                "WTK.MediaForge.Windows.MediaForgeWindows"
            ]
        };
}
