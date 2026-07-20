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
        private StudioApplicationSession? _session;

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
                _session = StudioBootstrapper.CreateRuntimeSession();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _session.Shell,
                };
                desktop.Exit += OnDesktopExit;
                _ = InitializeRuntimeAsync(_session);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async Task InitializeRuntimeAsync(StudioApplicationSession session)
        {
            try
            {
                await session.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError($"Studio capability initialization failed: {exception}");
            }
        }

        private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
        {
            if (_session is null)
                return;

            try
            {
                await _session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError($"Studio runtime shutdown failed: {exception}");
            }
        }
    }
}
