using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Core.Tests;

public class ContentFitLayoutTests
{
    [Fact]
    public void ComputeFitRect_centers_wider_destination()
    {
        var fit = ContentFitLayout.ComputeFitRect(64, 64, 128, 64);

        Assert.Equal(32, fit.X);
        Assert.Equal(0, fit.Y);
        Assert.Equal(64, fit.Width);
        Assert.Equal(64, fit.Height);
    }

    [Fact]
    public void ComputeFitRect_letterboxes_taller_destination()
    {
        var fit = ContentFitLayout.ComputeFitRect(1920, 1080, 800, 600);

        Assert.Equal(0, fit.X);
        Assert.Equal(75, fit.Y);
        Assert.Equal(800, fit.Width);
        Assert.Equal(450, fit.Height);
    }

    [Fact]
    public void ComputeFitRect_returns_full_destination_when_source_size_is_zero()
    {
        var fit = ContentFitLayout.ComputeFitRect(0, 64, 128, 64);

        Assert.Equal(new ContentFitRect(0, 0, 128, 64), fit);
    }
}
