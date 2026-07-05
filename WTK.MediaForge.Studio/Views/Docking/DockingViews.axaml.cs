using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.ViewModels.Docking;

namespace WTK.MediaForge.Studio.Views.Docking;

public partial class StudioDockPanelHost : UserControl
{
    private StudioFloatingPanelWindow? _floatingWindow;
    private StudioDockPanelViewModel? _viewModel;
    private bool _closingFloatingWindow;

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
        if (e.PropertyName != nameof(StudioDockPanelViewModel.IsFloating) || _viewModel is null)
        {
            return;
        }

        if (_viewModel.IsFloating && _floatingWindow is null)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            _floatingWindow = new StudioFloatingPanelWindow
            {
                DataContext = _viewModel
            };
            _floatingWindow.Closed += (_, _) =>
            {
                _floatingWindow = null;
                if (!_closingFloatingWindow && _viewModel is not null)
                {
                    _viewModel.IsFloating = false;
                }
            };
            if (owner is not null)
            {
                _floatingWindow.Show(owner);
            }
            else
            {
                _floatingWindow.Show();
            }
        }
        else if (!_viewModel.IsFloating && _floatingWindow is not null)
        {
            _closingFloatingWindow = true;
            try
            {
                _floatingWindow.Close();
            }
            finally
            {
                _closingFloatingWindow = false;
                _floatingWindow = null;
            }
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

    private void OnFloatingTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
