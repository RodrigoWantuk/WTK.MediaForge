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
        var runtimeFactory = new WindowsMediaForgeRuntimeFactory();
        StudioRuntimeHost.Configure(runtimeFactory);
        StudioPreviewHostFactory.Configure(() =>
        {
            var control = new WindowsHostedPreviewControl(() => runtimeFactory.Engine);
            runtimeFactory.EngineCreated += (_, engine) => control.NotifyEngineCreated(engine);
            if (runtimeFactory.Engine is { } engine)
                control.NotifyEngineCreated(engine);
            return control;
        });
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
