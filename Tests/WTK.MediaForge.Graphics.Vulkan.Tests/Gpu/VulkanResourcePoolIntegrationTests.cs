using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests.Gpu;

[Trait("Category", "GPU")]
public sealed class VulkanResourcePoolIntegrationTests
{
    [Fact]
    public void Renderer_offscreen_binding_uses_gpu_resource_pool_factory()
    {
        var guard = new RenderThreadGuard();
        Assert.True(
            MediaForgeVulkanRenderer.TryCreate(guard, diagnostics: null, NullVulkanRendererFaultInjector.Instance, out var renderer));
        Assert.NotNull(renderer);

        try
        {
            guard.BindToCurrentThread();
            var pool = renderer!.GpuResourcePoolForTests;
            var before = pool.FactoryCreateCount;

            renderer.BindOutput(new RenderOutputBindingSnapshot
            {
                OutputId = RenderOutputId.New(),
                TargetKind = RenderTargetKind.Offscreen,
                SurfaceSize = new FrameSize(640, 360),
                BindingVersion = 1
            });

            Assert.True(pool.FactoryCreateCount > before);
            Assert.Equal(1, renderer.OffscreenTargetCount);
        }
        finally
        {
            guard.Clear();
            renderer!.Dispose();
        }
    }
}
