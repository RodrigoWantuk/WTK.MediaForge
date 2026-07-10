using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class DecodedFrameToSourceFrameAdapterTests
{
    [Fact]
    public void CreateSourceFrameLease_preserves_source_id_size_frame_number_and_pts()
    {
        var factory = new RenderableTextureFactory();
        using var pool = new GpuResourcePool(factory);
        var sourceId = SourceId.New();
        var pts = TimeSpan.FromMilliseconds(125);
        var decoded = new DecodedGpuFrame(
            pool.AcquireTexture(CreateDescriptor(1280, 720)),
            pts,
            TimeSpan.FromMilliseconds(33));

        using var sourceLease = DecodedFrameToSourceFrameAdapter.Instance.CreateSourceFrameLease(
            decoded,
            sourceId,
            frameNumber: 17);

        var physical = Assert.IsType<RenderablePhysicalTexture>(factory.LastPhysical);
        Assert.Throws<ObjectDisposedException>(() => _ = decoded.TextureLease);
        Assert.Equal(sourceId, sourceLease.Frame.SourceId);
        Assert.Equal(17, sourceLease.Frame.FrameNumber);
        Assert.Equal(new FrameSize(1280, 720), sourceLease.Frame.TextureSize);
        Assert.Equal(new FrameSize(1280, 720), sourceLease.Frame.LogicalSize);
        Assert.Equal(new MediaTime(pts.Ticks * 100), sourceLease.Frame.Timestamp);
        Assert.Equal(GpuFrameBackend.D3D11SharedTexture, sourceLease.Frame.Backend);
        Assert.Same(physical.Handle, sourceLease.Frame.Handle);
        Assert.Equal(1, pool.ActiveTextureCount);

        sourceLease.Dispose();

        Assert.Equal(0, pool.ActiveTextureCount);
        Assert.Equal(1, physical.FinalizeCount);
    }

    [Fact]
    public void CreateSourceFrameLease_releases_texture_when_decoded_resource_is_not_renderable()
    {
        var factory = new NonRenderableTextureFactory();
        using var pool = new GpuResourcePool(factory);
        var decoded = new DecodedGpuFrame(
            pool.AcquireTexture(CreateDescriptor(320, 180)),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(33));

        var ex = Assert.Throws<NotSupportedException>(() =>
            DecodedFrameToSourceFrameAdapter.Instance.CreateSourceFrameLease(
                decoded,
                SourceId.New(),
                frameNumber: 1));

        Assert.Contains("renderable GPU frame handle", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ObjectDisposedException>(() => _ = decoded.TextureLease);
        Assert.Equal(0, pool.ActiveTextureCount);
        Assert.Equal(1, factory.LastPhysical!.FinalizeCount);
    }

    [Fact]
    public void RenderFrameSnapshot_can_bind_decoded_source_frame_without_knowing_decoder()
    {
        var factory = new RenderableTextureFactory();
        using var pool = new GpuResourcePool(factory);
        var sourceId = SourceId.New();
        using var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(new DecodedFrameProvider(sourceId, pool));

        using var result = RenderFrameSnapshotFactory.Build(
            CreateProjectState(sourceId),
            runtime);
        using var snapshot = result.TakeSnapshot();

        Assert.NotNull(snapshot);
        Assert.Empty(result.Diagnostics);
        Assert.Single(snapshot!.FrameLeases);

        var layer = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot.Canvases[0].Objects[0]);
        var frame = Assert.NotNull(layer.BoundFrame);
        var physical = Assert.IsType<RenderablePhysicalTexture>(factory.LastPhysical);
        Assert.Equal(sourceId, frame.SourceId);
        Assert.Same(physical.Handle, frame.Handle);
        Assert.Equal(new FrameSize(640, 360), frame.TextureSize);

        snapshot.Dispose();
        Assert.Equal(0, physical.FinalizeCount);

        runtime.Dispose();
        Assert.Equal(1, physical.FinalizeCount);
    }

    private static GpuTextureDescriptor CreateDescriptor(int width, int height) =>
        new()
        {
            Width = width,
            Height = height,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.ExternalImport,
            Recyclable = false
        };

    private static ProjectStateSnapshot CreateProjectState(SourceId sourceId) =>
        new()
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Decoded source",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 360) }
                        }
                    ]
                }
            ]
        };

    private sealed class DecodedFrameProvider(SourceId sourceId, GpuResourcePool pool) : IVideoFrameProvider
    {
        private long _frameNumber;
        private bool _published;

        public SourceId Id { get; } = sourceId;

        public string Name => "Decoded";

        public MediaSourceState State => MediaSourceState.Running;

        public Exception? LastError => null;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryAcquireLatestFrame(out GpuFrameLease lease)
        {
            if (_published)
            {
                lease = null!;
                return false;
            }

            _published = true;
            var decoded = new DecodedGpuFrame(
                pool.AcquireTexture(CreateDescriptor(640, 360)),
                TimeSpan.FromMilliseconds(42),
                TimeSpan.FromMilliseconds(33));

            lease = DecodedFrameToSourceFrameAdapter.Instance.CreateSourceFrameLease(
                decoded,
                Id,
                Interlocked.Increment(ref _frameNumber));
            return true;
        }
    }

    private sealed class RenderableTextureFactory : IGpuTextureFactory
    {
        public IGpuPhysicalResource? LastPhysical { get; private set; }

        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
        {
            _ = descriptor;
            LastPhysical = new RenderablePhysicalTexture();
            return LastPhysical;
        }
    }

    private sealed class NonRenderableTextureFactory : IGpuTextureFactory
    {
        public NonRenderablePhysicalTexture? LastPhysical { get; private set; }

        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
        {
            _ = descriptor;
            LastPhysical = new NonRenderablePhysicalTexture();
            return LastPhysical;
        }
    }

    private sealed class RenderablePhysicalTexture : IGpuPhysicalResource, IGpuFrameHandleProvider
    {
        private readonly TaskCompletionSource _fullyDisposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finalized;

        public FakeGpuFrameHandle Handle { get; } = new(GpuFrameBackend.D3D11SharedTexture);

        public int FinalizeCount => Volatile.Read(ref _finalized);

        public Task FullyDisposed => _fullyDisposed.Task;

        public IGpuFrameHandle FrameHandle => Handle;

        public bool TryFinalizePhysicalResources()
        {
            Interlocked.Increment(ref _finalized);
            _fullyDisposed.TrySetResult();
            return true;
        }
    }

    private sealed class NonRenderablePhysicalTexture : IGpuPhysicalResource
    {
        private readonly TaskCompletionSource _fullyDisposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finalized;

        public int FinalizeCount => Volatile.Read(ref _finalized);

        public Task FullyDisposed => _fullyDisposed.Task;

        public bool TryFinalizePhysicalResources()
        {
            Interlocked.Increment(ref _finalized);
            _fullyDisposed.TrySetResult();
            return true;
        }
    }

    private sealed class FakeGpuFrameHandle(GpuFrameBackend backend) : IGpuFrameHandle
    {
        public GpuFrameBackend Backend { get; } = backend;
    }
}
