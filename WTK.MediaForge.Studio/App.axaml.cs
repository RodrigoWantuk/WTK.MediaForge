using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Globalization;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.Views;

namespace WTK.MediaForge.Studio
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            var culture = new CultureInfo("pt-BR");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = StudioBootstrapper.CreateShellViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
