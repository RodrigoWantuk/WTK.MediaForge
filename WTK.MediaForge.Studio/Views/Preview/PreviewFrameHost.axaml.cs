using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed partial class PreviewFrameHost : UserControl
{
    public static readonly StyledProperty<object?> FrameSourceProperty =
        AvaloniaProperty.Register<PreviewFrameHost, object?>(nameof(FrameSource));

    public static readonly StyledProperty<bool> IsFrameLiveProperty =
        AvaloniaProperty.Register<PreviewFrameHost, bool>(nameof(IsFrameLive));

    public PreviewFrameHost()
    {
        AvaloniaXamlLoader.Load(this);
        if (NameScope.GetNameScope(this)?.Find<ContentControl>("NativeHost") is { } nativeHost)
            nativeHost.Content = StudioPreviewHostFactory.Create();
    }

    public object? FrameSource
    {
        get => GetValue(FrameSourceProperty);
        set => SetValue(FrameSourceProperty, value);
    }

    public bool IsFrameLive
    {
        get => GetValue(IsFrameLiveProperty);
        set => SetValue(IsFrameLiveProperty, value);
    }
}
