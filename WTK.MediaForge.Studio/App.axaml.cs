using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Views;

namespace WTK.MediaForge.Studio
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = StudioDesignData.CreateShellViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
