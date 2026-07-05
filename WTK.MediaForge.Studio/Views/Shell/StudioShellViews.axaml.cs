using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace WTK.MediaForge.Studio.Views.Shell;

public partial class StudioTitleBarView : UserControl
{
    public StudioTitleBarView() => AvaloniaXamlLoader.Load(this);

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (OwnerWindow is not { } window)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized(window);
            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (OwnerWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (OwnerWindow is { } window)
        {
            ToggleMaximized(window);
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OwnerWindow?.Close();
    }

    private static void ToggleMaximized(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}

public partial class StudioToolbarView : UserControl
{
    public StudioToolbarView() => AvaloniaXamlLoader.Load(this);
}

public partial class ProjectExplorerView : UserControl
{
    public ProjectExplorerView() => AvaloniaXamlLoader.Load(this);
}

public partial class PreviewHeaderView : UserControl
{
    public PreviewHeaderView() => AvaloniaXamlLoader.Load(this);
}

public partial class PreviewCanvasView : UserControl
{
    public PreviewCanvasView() => AvaloniaXamlLoader.Load(this);
}

public partial class InspectorView : UserControl
{
    public InspectorView() => AvaloniaXamlLoader.Load(this);
}

public partial class ProductionPanelView : UserControl
{
    public ProductionPanelView() => AvaloniaXamlLoader.Load(this);
}

public partial class BottomWorkbenchView : UserControl
{
    public BottomWorkbenchView() => AvaloniaXamlLoader.Load(this);
}

public partial class StudioStatusBarView : UserControl
{
    public StudioStatusBarView() => AvaloniaXamlLoader.Load(this);
}
