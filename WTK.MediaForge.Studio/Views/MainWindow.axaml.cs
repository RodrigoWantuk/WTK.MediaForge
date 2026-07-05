using Avalonia.Controls;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.Views.Settings;

namespace WTK.MediaForge.Studio.Views
{
    public partial class MainWindow : Window
    {
        private StudioShellViewModel? _shell;
        private SettingsWindow? _settingsWindow;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_shell is not null)
            {
                _shell.SettingsRequested -= OnSettingsRequested;
            }

            _settingsWindow?.Close();
            base.OnClosed(e);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_shell is not null)
            {
                _shell.SettingsRequested -= OnSettingsRequested;
            }

            _shell = DataContext as StudioShellViewModel;
            if (_shell is not null)
            {
                _shell.SettingsRequested += OnSettingsRequested;
            }
        }

        private void OnSettingsRequested(object? sender, EventArgs e)
        {
            if (_shell is null)
            {
                return;
            }

            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow
            {
                DataContext = new SettingsViewModel(_shell)
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show(this);
        }
    }
}
