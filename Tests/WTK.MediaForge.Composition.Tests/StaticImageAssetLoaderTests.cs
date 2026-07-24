using WTK.MediaForge.Composition.Sources;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class StaticImageAssetFormatsTests
{
    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo.jpg")]
    [InlineData("logo.jpeg")]
    public void IsSupportedExtension_accepts_png_and_jpeg(string fileName)
    {
        Assert.True(StaticImageAssetFormats.IsSupportedExtension(fileName));
    }

    [Theory]
    [InlineData("logo.webp")]
    [InlineData("logo.gif")]
    public void IsSupportedExtension_rejects_unapproved_formats(string fileName)
    {
        Assert.False(StaticImageAssetFormats.IsSupportedExtension(fileName));
    }
}
