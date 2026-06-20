using System.Reflection;
using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.Vulkan;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public class PublicApiSurfaceTests
{
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
}
