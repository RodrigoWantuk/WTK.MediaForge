using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class ImageFileSourceRuntimeTests
{
    [Fact]
    public void MarkGpuUploaded_preserves_size_and_format_after_cpu_asset_is_released()
    {
        using var runtime = new ImageFileSourceRuntime(
            SourceId.New(),
            "Logo",
            new ImageFileSourceSettings { Path = "logo.png" },
            new AssetManager(new TestStaticImageAssetDecoder(
                new FrameSize(320, 180),
                RenderPixelFormat.Rgba8Unorm)));

        runtime.LoadAsset();
        Assert.NotNull(runtime.LoadedAsset);

        runtime.MarkGpuUploaded();

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
        Assert.Equal(new FrameSize(320, 180), frame.Value.LogicalSize);
        Assert.Equal(new FrameSize(320, 180), frame.Value.TextureSize);
    }

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
}
