using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.ViewModels.Docking;

namespace WTK.MediaForge.Studio.Views.Docking;

public partial class StudioDockPanelHost : UserControl
{
    private StudioFloatingPanelWindow? _floatingWindow;
    private StudioDockPanelViewModel? _viewModel;

    public StudioDockPanelHost()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnPanelPropertyChanged;
        }

        base.OnDataContextChanged(e);
        _viewModel = DataContext as StudioDockPanelViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnPanelPropertyChanged;
        }
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioDockPanelViewModel.IsFloating)
            && _viewModel?.IsFloating == true
            && _floatingWindow is null)
        {
            _floatingWindow = new StudioFloatingPanelWindow
            {
                DataContext = _viewModel
            };
            _floatingWindow.Closed += (_, _) =>
            {
                _floatingWindow = null;
                if (_viewModel is not null)
                {
                    _viewModel.IsFloating = false;
                }
            };
            _floatingWindow.Show();
        }
    }
}

public partial class StudioFloatingPanelWindow : Window
{
    public StudioFloatingPanelWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is StudioDockPanelViewModel panel)
        {
            panel.IsFloating = false;
        }

        base.OnClosing(e);
    }
}
