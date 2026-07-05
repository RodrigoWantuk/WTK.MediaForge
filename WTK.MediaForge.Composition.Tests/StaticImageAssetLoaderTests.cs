using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class StaticImageAssetLoaderTests
{
    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo.jpg")]
    [InlineData("logo.jpeg")]
    public void IsSupportedExtension_accepts_png_and_jpeg(string fileName)
    {
        Assert.True(StaticImageAssetLoader.IsSupportedExtension(fileName));
    }

    [Theory]
    [InlineData("logo.webp")]
    [InlineData("logo.gif")]
    public void IsSupportedExtension_rejects_unapproved_formats(string fileName)
    {
        Assert.False(StaticImageAssetLoader.IsSupportedExtension(fileName));
    }

    [Fact]
    public void Load_missing_file_throws()
    {
        var loader = new StaticImageAssetLoader();
        Assert.Throws<FileNotFoundException>(() => loader.Load("missing-file.png"));
    }
}
