using System.Runtime.Versioning;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[SupportedOSPlatform("windows")]
public sealed class WindowsStaticImageAssetDecoderTests
{
    [Fact]
    public void Decode_missing_file_throws()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var decoder = new WindowsStaticImageAssetDecoder();

        Assert.Throws<FileNotFoundException>(() =>
            decoder.Decode(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png")));
    }

    [Fact]
    public void Decode_png_returns_static_cpu_asset_metadata_and_pixels()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"mf-static-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));

        try
        {
            var asset = new WindowsStaticImageAssetDecoder().Decode(path);

            Assert.Equal(path, asset.Path);
            Assert.Equal(new FrameSize(1, 1), asset.Size);
            Assert.Equal(RenderPixelFormat.Rgba8Unorm, asset.PixelFormat);
            Assert.Equal(MediaTransportKind.StaticCpuAsset, asset.TransportKind);
            Assert.Equal(4, asset.Pixels.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("logo.png", true)]
    [InlineData("logo.jpeg", true)]
    [InlineData("logo.webp", false)]
    public void Static_image_format_contract_is_platform_independent(string path, bool supported)
    {
        Assert.Equal(supported, StaticImageAssetFormats.IsSupportedExtension(path));
    }
}
