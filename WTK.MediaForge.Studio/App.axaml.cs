using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Globalization;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.Views;

namespace WTK.MediaForge.Studio
{
    public partial class App : Application
    {
        private StudioApplicationSession? _session;
        private CancellationTokenSource? _lifetimeCancellation;
        private Task? _sessionInitialization;

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
                _lifetimeCancellation = new CancellationTokenSource();
                var window = new MainWindow();
                desktop.MainWindow = window;
                desktop.Exit += OnDesktopExit;
                _sessionInitialization = InitializeRuntimeAsync(window, _lifetimeCancellation.Token);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitializeRuntimeAsync(MainWindow window, CancellationToken cancellationToken)
        {
            try
            {
                var session = await StudioBootstrapper.CreateRuntimeSessionAsync(
                    StudioRuntimeHost.GetRequiredFactory(), cancellationToken);
                _session = session;
                window.DataContext = session.Shell;
                await session.InitializeAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError($"Studio capability initialization failed: {exception}");
            }
        }

        private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
        {
            try
            {
                _lifetimeCancellation?.Cancel();
                if (_sessionInitialization is not null)
                    await _sessionInitialization.ConfigureAwait(false);
                if (_session is not null)
                    await _session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError($"Studio runtime shutdown failed: {exception}");
            }
            finally
            {
                _lifetimeCancellation?.Dispose();
                _lifetimeCancellation = null;
            }
        }
    }
}
