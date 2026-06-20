using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class VulkanOffscreenRenderTargetTests
{
    [Fact]
    public void BindOutput_offscreen_creates_render_target()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var binding = CreateOffscreenBinding(outputId, width: 640, height: 480);

                renderer.Backend.BindOutput(binding);

                Assert.Equal(1, renderer.Backend.OffscreenTargetCount);
                Assert.True(renderer.Backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(640u, size.Width);
                Assert.Equal(480u, size.Height);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void BindOutput_offscreen_rebind_replaces_existing_target()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();

                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 320, 240));
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 1280, 720));

                Assert.Equal(1, renderer.Backend.OffscreenTargetCount);
                Assert.True(renderer.Backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(1280u, size.Width);
                Assert.Equal(720u, size.Height);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void ResizeOutput_offscreen_updates_target_dimensions()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 640, 480));

                renderer.Backend.ResizeOutput(outputId, new FrameSize(800, 600));

                Assert.Equal(1, renderer.Backend.OffscreenTargetCount);
                Assert.True(renderer.Backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(800u, size.Width);
                Assert.Equal(600u, size.Height);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void ResizeOutput_offscreen_rejects_zero_dimensions_and_keeps_existing_target()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 640, 480));

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    renderer.Backend.ResizeOutput(outputId, new FrameSize(0, 600)));

                Assert.Equal(1, renderer.Backend.OffscreenTargetCount);
                Assert.True(renderer.Backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(640u, size.Width);
                Assert.Equal(480u, size.Height);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void UnbindOutput_offscreen_disposes_target()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 640, 480));

                renderer.Backend.UnbindOutput(outputId);

                Assert.Equal(0, renderer.Backend.OffscreenTargetCount);
                Assert.False(renderer.Backend.TryGetOffscreenTargetSize(outputId, out _));
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void BindOutput_offscreen_rejects_zero_dimensions()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var binding = CreateOffscreenBinding(outputId, width: 0, height: 480);

                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Backend.BindOutput(binding));
                Assert.Equal(0, renderer.Backend.OffscreenTargetCount);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static RenderOutputBindingSnapshot CreateOffscreenBinding(
        RenderOutputId outputId,
        uint width,
        uint height) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            NativeHandle = 0,
            SurfaceSize = new FrameSize(width, height),
            BindingVersion = 1
        };

    private static bool TryCreateRenderer(out TestRendererContext? context)
    {
        context = null;

        try
        {
            var guard = new RenderThreadGuard();
            if (!MediaForgeVulkanRenderer.TryCreate(
                    guard,
                    diagnostics: null,
                    NullVulkanRendererFaultInjector.Instance,
                    out var backend) ||
                backend is null)
            {
                return false;
            }

            context = new TestRendererContext(guard, backend);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TestRendererContext : IDisposable
    {
        public TestRendererContext(RenderThreadGuard guard, MediaForgeVulkanRenderer backend)
        {
            Guard = guard;
            Backend = backend;
        }

        public RenderThreadGuard Guard { get; }

        public MediaForgeVulkanRenderer Backend { get; }

        public void Dispose() => Backend.Dispose();
    }
}
