using Vortice.DXGI;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class RenderFrameSnapshotGpuFramesTests
{
    [Fact]
    public void CollectD3D11SharedTextures_deduplicates_by_texture_key()
    {
        if (!TryCreateDevice(out var device))
            return;

        using (device)
        {
            var textureId = GpuTextureId.New();
            using var first = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);
            using var second = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);

            Assert.NotEqual(
                first.SharedHandle.DangerousGetHandleForInterop(),
                second.SharedHandle.DangerousGetHandleForInterop());

            var snapshot = CreateSnapshotWithHandles(first, second);

            var collected = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(snapshot);

            Assert.Single(collected);
            Assert.Equal(textureId, collected[0].TextureId);
        }
    }

    [Fact]
    public void Duplicate_layers_same_bound_frame_collect_one_handle()
    {
        if (!TryCreateDevice(out var device))
            return;

        using (device)
        using (var handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        {
            var frame = ToFrame(handle);
            var snapshot = new RenderFrameSnapshot
            {
                ProjectStateVersion = 1,
                Canvases =
                [
                    new RenderCanvasSnapshot
                    {
                        Id = CanvasId.New(),
                        Name = "Main",
                        Size = handle.TextureSize,
                        Objects =
                        [
                            CreateLayer(frame, "LayerA"),
                            CreateLayer(frame, "LayerB")
                        ]
                    }
                ]
            };

            var collected = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(snapshot);

            Assert.Single(collected);
            Assert.Same(handle, collected[0]);
        }
    }

    [Fact]
    public void CollectD3D11SharedTextures_ignores_sources_without_a_physical_acquisition()
    {
        if (!TryCreateDevice(out var device))
            return;

        using (device)
        using (var acquired = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        using (var unplanned = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        {
            var acquiredFrame = ToFrame(acquired);
            var unplannedFrame = ToFrame(unplanned);
            var snapshot = new RenderFrameSnapshot
            {
                ProjectStateVersion = 1,
                Canvases =
                [
                    new RenderCanvasSnapshot
                    {
                        Id = CanvasId.New(),
                        Name = "Main",
                        Size = acquired.TextureSize,
                        Objects =
                        [
                            CreateLayer(acquiredFrame, "Acquired"),
                            CreateLayer(unplannedFrame, "Unplanned")
                        ]
                    }
                ]
            };

            var collected = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(
                snapshot,
                [
                    new PhysicalRenderGraphOperation
                    {
                        Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                        Key = "source:acquired",
                        SourceId = acquiredFrame.SourceId
                    }
                ]);

            var handle = Assert.Single(collected);
            Assert.Same(acquired, handle);
        }
    }

    [Fact]
    public void Physical_source_acquisition_rejects_divergent_external_textures_for_one_source()
    {
        if (!TryCreateDevice(out var device))
            return;

        using (device)
        using (var first = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        using (var second = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        {
            var sourceId = SourceId.New();
            var firstFrame = ToFrame(first) with { SourceId = sourceId };
            var secondFrame = ToFrame(second) with { SourceId = sourceId };
            var snapshot = new RenderFrameSnapshot
            {
                ProjectStateVersion = 1,
                Canvases =
                [
                    new RenderCanvasSnapshot
                    {
                        Id = CanvasId.New(),
                        Name = "Main",
                        Size = first.TextureSize,
                        Objects =
                        [
                            CreateLayer(firstFrame, "First"),
                            CreateLayer(secondFrame, "Second")
                        ]
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(
                    snapshot,
                    [
                        new PhysicalRenderGraphOperation
                        {
                            Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                            Key = "source:camera",
                            SourceId = sourceId
                        }
                    ]));

            Assert.Contains("resolved multiple external textures", exception.Message, StringComparison.Ordinal);
            Assert.Contains("source:camera", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Physical_source_acquisition_rejects_duplicate_operations_for_one_source()
    {
        var sourceId = SourceId.New();
        var snapshot = new RenderFrameSnapshot { ProjectStateVersion = 1 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(
                snapshot,
                [
                    new PhysicalRenderGraphOperation
                    {
                        Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                        Key = "source:first",
                        SourceId = sourceId
                    },
                    new PhysicalRenderGraphOperation
                    {
                        Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                        Key = "source:second",
                        SourceId = sourceId
                    }
                ]));

        Assert.Contains("more than one source acquisition", exception.Message, StringComparison.Ordinal);
    }

    private static RenderFrameSnapshot CreateSnapshotWithHandles(
        D3D11SharedTextureFrameHandle first,
        D3D11SharedTextureFrameHandle second)
    {
        var firstFrame = ToFrame(first);
        var secondFrame = ToFrame(second);

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = first.TextureSize,
                    Objects =
                    [
                        CreateLayer(firstFrame, "Layer1"),
                        CreateLayer(secondFrame, "Layer2")
                    ]
                }
            ]
        };
    }

    private static GpuFrameReference ToFrame(D3D11SharedTextureFrameHandle handle) =>
        new()
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = handle,
            TextureSize = handle.TextureSize,
            LogicalSize = handle.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };

    private static RenderSourceLayerDrawObjectSnapshot CreateLayer(GpuFrameReference frame, string name) =>
        new()
        {
            Id = DrawObjectId.New(),
            Name = name,
            SourceId = frame.SourceId,
            Transform = new Transform2D
            {
                Size = new CanvasSize(frame.TextureSize.Width, frame.TextureSize.Height)
            },
            BoundFrame = frame
        };

    private static bool TryCreateDevice(out D3D11GpuDevice device)
    {
        device = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
