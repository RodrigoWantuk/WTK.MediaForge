using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
