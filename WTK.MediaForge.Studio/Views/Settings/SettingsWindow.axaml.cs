using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Views.Settings;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as SettingsViewModel;
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
