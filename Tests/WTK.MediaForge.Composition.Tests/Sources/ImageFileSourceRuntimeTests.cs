using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class ImageFileSourceRuntimeTests
{
    [Fact]
    public async Task MarkGpuUploaded_preserves_size_and_format_after_cpu_asset_is_released()
    {
        using var runtime = new ImageFileSourceRuntime(
            SourceId.New(),
            "Logo",
            new ImageFileSourceSettings { Path = "logo.png" },
            new AssetManager(new TestStaticImageAssetDecoder(
                new FrameSize(320, 180),
                RenderPixelFormat.Rgba8Unorm)));

        await runtime.StartAsync(CancellationToken.None);
        Assert.NotNull(runtime.LoadedAsset);

        var handle = new TestGpuFrameHandle(GpuFrameBackend.D3D11SharedTexture);
        runtime.MarkGpuUploaded(handle);

        Assert.True(runtime.IsGpuUploaded);
        Assert.Null(runtime.LoadedAsset);
        Assert.Equal(new FrameSize(320, 180), runtime.UploadedSize);
        Assert.Equal(RenderPixelFormat.Rgba8Unorm, runtime.UploadedPixelFormat);

        Assert.True(runtime.TryCreateGpuFrameReference(
            out var frame,
            new RenderFrameContext(
                FrameNumber: 7,
                PresentationTime: TimeSpan.FromMilliseconds(33),
                DeltaTime: TimeSpan.FromMilliseconds(16),
                TargetFps: 60,
                CancellationToken.None)));

        Assert.NotNull(frame);
        Assert.Equal(GpuFrameBackend.D3D11SharedTexture, frame.Value.Backend);
        Assert.Same(handle, frame.Value.Handle);
        Assert.Equal(new FrameSize(320, 180), frame.Value.LogicalSize);
        Assert.Equal(new FrameSize(320, 180), frame.Value.TextureSize);
    }

    [Fact]
    public async Task Stop_clears_gpu_upload_state_and_blocks_frame_reference()
    {
        using var runtime = CreateRuntime();
        await runtime.StartAsync(CancellationToken.None);
        runtime.MarkGpuUploaded(new TestGpuFrameHandle(GpuFrameBackend.D3D11SharedTexture));

        await runtime.StopAsync(CancellationToken.None);

        Assert.False(runtime.IsGpuUploaded);
        Assert.Null(runtime.UploadedSize);
        Assert.Null(runtime.UploadedPixelFormat);
        Assert.False(runtime.TryCreateGpuFrameReference(out var frame, CreateContext()));
        Assert.Null(frame);
    }

    [Fact]
    public async Task Dispose_clears_uploaded_handle_metadata()
    {
        var runtime = CreateRuntime();
        await runtime.StartAsync(CancellationToken.None);
        runtime.MarkGpuUploaded(new TestGpuFrameHandle(GpuFrameBackend.D3D11SharedTexture));

        runtime.Dispose();

        Assert.False(runtime.IsGpuUploaded);
        Assert.Null(runtime.UploadedSize);
        Assert.Null(runtime.UploadedPixelFormat);
    }

    [Fact]
    public void TryCreateGpuFrameReference_returns_false_when_source_is_not_running()
    {
        using var runtime = CreateRuntime();

        runtime.LoadAsset();
        runtime.MarkGpuUploaded(new TestGpuFrameHandle(GpuFrameBackend.D3D11SharedTexture));

        Assert.False(runtime.TryCreateGpuFrameReference(out var frame, CreateContext()));
        Assert.Null(frame);
    }

    private static ImageFileSourceRuntime CreateRuntime() =>
        new(
            SourceId.New(),
            "Logo",
            new ImageFileSourceSettings { Path = "logo.png" },
            new AssetManager(new TestStaticImageAssetDecoder(
                new FrameSize(320, 180),
                RenderPixelFormat.Rgba8Unorm)));

    private static RenderFrameContext CreateContext() =>
        new(
            FrameNumber: 7,
            PresentationTime: TimeSpan.FromMilliseconds(33),
            DeltaTime: TimeSpan.FromMilliseconds(16),
            TargetFps: 60,
            CancellationToken.None);

    private sealed class TestStaticImageAssetDecoder(
        FrameSize size,
        RenderPixelFormat pixelFormat) : IStaticImageAssetDecoder
    {
        public StaticCpuAsset Decode(string path) =>
            new()
            {
                Path = path,
                Size = size,
                PixelFormat = pixelFormat,
                Pixels = new byte[checked((int)(size.Width * size.Height * 4))],
                TransportKind = MediaTransportKind.StaticCpuAsset
            };
    }

    private sealed class TestGpuFrameHandle(GpuFrameBackend backend) : IGpuFrameHandle
    {
        public GpuFrameBackend Backend { get; } = backend;
    }
}
