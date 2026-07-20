using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Windows.Tests.Media;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

public sealed class WindowsVideoFileSourceProviderFactoryTests
{
    [Fact]
    public void Video_file_provider_uses_product_decoder_by_default()
    {
        var decoder = new FakeHardwareFileVideoDecoder();
        var factory = new WindowsVideoFileSourceProviderFactory(
            decoderFactory: _ => decoder);

        Assert.True(factory.CanCreate(MediaSourceTypes.VideoFile));
        using var provider = Assert.IsAssignableFrom<IDisposable>(
            factory.CreateProvider(CreateSourceDefinition(SourceId.New(), "product-default.mp4")));
    }

    [Fact]
    public void Product_video_file_provider_can_be_enabled_with_product_decoder_factory()
    {
        var decoder = new FakeHardwareFileVideoDecoder();
        var factory = new WindowsVideoFileSourceProviderFactory(
            enableProductProvider: true,
            decoderFactory: _ => decoder);

        Assert.True(factory.CanCreate(MediaSourceTypes.VideoFile));
        using var provider = Assert.IsAssignableFrom<IDisposable>(
            factory.CreateProvider(CreateSourceDefinition(SourceId.New(), "product.mp4")));
    }

    [Fact]
    public async Task Product_video_file_provider_decodes_to_renderable_gpu_source_frame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mf-video-source-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, MinimalMp4TestAsset.CreateAnnexBBytes());

        var decoder = new FakeHardwareFileVideoDecoder();
        var sourceId = SourceId.New();
        var factory = new WindowsVideoFileSourceProviderFactory(
            decoderFactory: _ => decoder);
        var provider = factory.CreateProvider(CreateSourceDefinition(sourceId, path));

        try
        {
            Assert.True(factory.CanCreate(MediaSourceTypes.VideoFile));

            await provider.StartAsync(CancellationToken.None);
            Assert.Equal(MediaSourceState.Running, provider.State);

            using var lease = await WaitForFrameAsync(provider);
            var physical = Assert.IsType<RenderablePhysicalTexture>(decoder.TextureFactory.LastPhysical);

            Assert.Equal(sourceId, lease.Frame.SourceId);
            Assert.Equal(GpuFrameBackend.D3D11SharedTexture, lease.Frame.Backend);
            Assert.Same(physical.Handle, lease.Frame.Handle);
            Assert.Equal(new FrameSize(160, 90), lease.Frame.TextureSize);
            Assert.Equal(new FrameSize(160, 90), lease.Frame.LogicalSize);
            Assert.Equal(new MediaTime(TimeSpan.FromMilliseconds(250).Ticks * 100), lease.Frame.Timestamp);
            Assert.Equal(0, physical.FinalizeCount);

            lease.Dispose();
            Assert.Equal(0, physical.FinalizeCount);

            await provider.StopAsync(CancellationToken.None);
            Assert.Equal(MediaSourceState.Stopped, provider.State);
            Assert.Equal(1, physical.FinalizeCount);
        }
        finally
        {
            if (provider is IDisposable disposable)
                disposable.Dispose();

            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static MediaForgeSourceDefinition CreateSourceDefinition(SourceId sourceId, string path) =>
        new()
        {
            Id = sourceId,
            Name = "Video",
            TypeId = MediaSourceTypes.VideoFile,
            Settings = MediaSourceSettingsSerializer.ToJson(new VideoFileSourceSettings
            {
                Path = path
            })
        };

    private static async Task<GpuFrameLease> WaitForFrameAsync(IVideoFrameProvider provider)
    {
        var deadline = Environment.TickCount64 + (long)TimeSpan.FromSeconds(2).TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (provider.TryAcquireLatestFrame(out var lease))
                return lease;

            if (provider.State == MediaSourceState.Failed)
                throw new InvalidOperationException("Provider failed before publishing a frame.", provider.LastError);

            await Task.Delay(5);
        }

        throw new TimeoutException("Video file provider did not publish a frame before the timeout.");
    }

    private sealed class FakeHardwareFileVideoDecoder : IHardwareFileVideoDecoder
    {
        private readonly GpuResourcePool _pool;
        private int _decodeAttempts;

        public FakeHardwareFileVideoDecoder()
        {
            TextureFactory = new RenderableTextureFactory();
            _pool = new GpuResourcePool(TextureFactory);
        }

        public RenderableTextureFactory TextureFactory { get; }

        public HardwareDecoderInfo Info { get; } = new()
        {
            Name = "Fake",
            Codec = EncodedVideoCodec.H264,
            Backend = "Fake",
            ProducesGpuSurface = true
        };

        public ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink)
        {
            _ = context;
            _ = auditSink;
            return ValueTask.CompletedTask;
        }

        public ValueTask<DecodedGpuFrame?> DecodeNextFrameAsync(
            FileDecodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            _ = context;
            _ = auditSink;

            if (Interlocked.Increment(ref _decodeAttempts) != 1)
                return ValueTask.FromResult<DecodedGpuFrame?>(null);

            var lease = _pool.AcquireTexture(new GpuTextureDescriptor
            {
                Width = 160,
                Height = 90,
                Format = "B8G8R8A8_UNORM",
                Usage = GpuTextureUsage.ExternalImport,
                Recyclable = false
            });

            return ValueTask.FromResult<DecodedGpuFrame?>(
                new DecodedGpuFrame(
                    lease,
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromMilliseconds(5)));
        }

        public ValueTask FlushAsync(IMediaTransportAuditSink auditSink)
        {
            _ = auditSink;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _pool.Dispose();
            return ValueTask.CompletedTask;
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

    private sealed class FakeGpuFrameHandle(GpuFrameBackend backend) : IGpuFrameHandle
    {
        public GpuFrameBackend Backend { get; } = backend;
    }
}
