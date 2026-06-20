using Vortice.DXGI;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class Cp1OffscreenCompositionTests
{
    [Fact]
    public void Cp1_single_source_layer_renders_to_offscreen()
    {
        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        if (!TryCreateRenderer(out var context))
            return;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(new RenderOutputBindingSnapshot
                {
                    OutputId = outputId,
                    TargetKind = RenderTargetKind.Offscreen,
                    SurfaceSize = new FrameSize(1280, 720),
                    BindingVersion = 1
                });

                var snapshot = CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);
                submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None).AsTask().GetAwaiter().GetResult();
                submission.DisposeCompleted();

                Assert.Equal(1, backend.SubmitCount);
                Assert.True(backend.TryGetOffscreenTargetSize(outputId, out var size));
                Assert.Equal(1280u, size.Width);
                Assert.Equal(720u, size.Height);

                snapshot.Dispose();
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Offscreen_target_survives_unbind_until_submission_fence_completes()
    {
        if (!TryCreateSharedTexture(out var device, out var sharedHandle))
            return;

        using var deviceScope = device;
        using var handleScope = sharedHandle;

        VulkanOffscreenRenderTargetLifetime.Reset();

        if (!TryCreateRenderer(out var context))
            return;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var canvasId = CanvasId.New();

                backend.BindOutput(new RenderOutputBindingSnapshot
                {
                    OutputId = outputId,
                    TargetKind = RenderTargetKind.Offscreen,
                    SurfaceSize = new FrameSize(640, 480),
                    BindingVersion = 1
                });

                Assert.Equal(1, VulkanOffscreenRenderTargetLifetime.LiveCount);

                var snapshot = CreateCp1Snapshot(sharedHandle, canvasId, outputId);
                var submission = backend.Submit(snapshot);

                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);

                backend.UnbindOutput(outputId);

                Assert.Equal(0, backend.OffscreenTargetCount);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(0, VulkanOffscreenRenderTargetLifetime.DisposeCount);

                submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None).AsTask().GetAwaiter().GetResult();
                submission.DisposeCompleted();

                Assert.Equal(0, backend.TextureRegistryActiveLeaseCount);
                Assert.Equal(0, VulkanOffscreenRenderTargetLifetime.LiveCount);
                Assert.Equal(2, VulkanOffscreenRenderTargetLifetime.DisposeCount);

                snapshot.Dispose();
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static RenderFrameSnapshot CreateCp1Snapshot(
        D3D11SharedTextureFrameHandle sharedHandle,
        CanvasId canvasId,
        RenderOutputId outputId)
    {
        var frame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = sharedHandle,
            TextureSize = sharedHandle.TextureSize,
            LogicalSize = sharedHandle.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = canvasId,
                    Name = "Program",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Desktop",
                            SourceId = frame.SourceId,
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) },
                            BoundFrame = frame
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Offscreen",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1280, 720),
                    CanvasLayoutMode = LayoutMode.Fit
                }
            ]
        };
    }

    private static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = [],
            Outputs = []
        };

    private static bool TryCreateSharedTexture(out D3D11GpuDevice device, out D3D11SharedTextureFrameHandle handle)
    {
        device = null!;
        handle = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            return true;
        }
        catch
        {
            return false;
        }
    }

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
