using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Views.Preview;

public partial class ResizeHandleControl : UserControl
{
    public static readonly StyledProperty<ResizeHandleKind> HandleKindProperty =
        AvaloniaProperty.Register<ResizeHandleControl, ResizeHandleKind>(nameof(HandleKind));

    public ResizeHandleControl()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ResizeHandleKind HandleKind
    {
        get => GetValue(HandleKindProperty);
        set => SetValue(HandleKindProperty, value);
    }
}

public partial class SelectionAdorner : UserControl
{
    public SelectionAdorner()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
