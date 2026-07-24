using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Runtime.InteropServices;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.Views;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioAppSmokeTests
{
    [Fact]
    public void Main_window_loads_with_shell_view_model_under_headless_avalonia()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(StudioHeadlessAppBuilder),
            AvaloniaTestIsolationLevel.PerTest);

        session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1366,
                Height = 768,
                DataContext = CreateShell()
            };

            try
            {
                window.Show();

                Assert.IsType<StudioShellViewModel>(window.DataContext);
                Assert.NotNull(window.Content);
                Assert.Equal("WTK MediaForge Studio", window.Title);
                Assert.True(window.MinWidth >= 1280);
                Assert.True(window.MinHeight >= 720);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Product_viewport_screenshots_render_nonblank_shell_content()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(StudioHeadlessAppBuilder),
            AvaloniaTestIsolationLevel.PerTest);

        session.Dispatch(() =>
        {
            foreach (var viewport in VisualQa.StudioVisualQaViewport.ProductTargets)
            {
                var window = new MainWindow
                {
                    Width = viewport.Width,
                    Height = viewport.Height,
                    DataContext = CreateShell()
                };

                try
                {
                    window.Show();
                    using var screenshot = window.CaptureRenderedFrame();
                    Assert.NotNull(screenshot);
                    Assert.Equal((int)viewport.Width, screenshot.PixelSize.Width);
                    Assert.Equal((int)viewport.Height, screenshot.PixelSize.Height);
                    AssertScreenshotContainsStructuredUi(screenshot, viewport.Name);
                }
                finally
                {
                    window.Close();
                }
            }
        }, CancellationToken.None);
    }

    private static void AssertScreenshotContainsStructuredUi(
        Avalonia.Media.Imaging.Bitmap screenshot,
        string viewportName)
    {
        var width = screenshot.PixelSize.Width;
        var height = screenshot.PixelSize.Height;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            screenshot.CopyPixels(
                new PixelRect(0, 0, width, height),
                pinned.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            pinned.Free();
        }

        var first = BitConverter.ToUInt32(pixels, 0);
        var distinctSamples = 0;
        var sampleStep = Math.Max(4, pixels.Length / 4096 & ~3);
        for (var offset = 0; offset <= pixels.Length - 4; offset += sampleStep)
        {
            if (BitConverter.ToUInt32(pixels, offset) != first)
                distinctSamples++;
        }

        Assert.True(
            distinctSamples >= 64,
            $"{viewportName} rendered as a blank or effectively uniform surface ({distinctSamples} varied samples)." );
    }

    private static StudioShellViewModel CreateShell()
    {
        var services = StudioServiceFactory.CreateFake(uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }
}

public static class StudioHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true,
                ShouldRenderOnUIThread = true
            })
            .WithInterFont();
    }
}
