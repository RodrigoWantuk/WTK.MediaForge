using Avalonia;
using WTK.MediaForge.Studio;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Windows;

namespace WTK.MediaForge.Studio.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StudioRuntimeHost.Configure(new WindowsMediaForgeRuntimeFactory());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .LogToTrace();
}
