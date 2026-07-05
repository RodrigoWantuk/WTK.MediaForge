using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed partial class PreviewEditorControl : UserControl
{
    private LayerItemViewModel? _activeLayer;
    private ResizeHandleKind _activeHandle = ResizeHandleKind.None;
    private Point _lastCanvasPoint;
    private bool _isDragging;
    private bool _isResizing;

    public PreviewEditorControl()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private PreviewCanvasViewModel? ViewModel => DataContext as PreviewCanvasViewModel;

    private Canvas? SceneCanvasControl => this.FindControl<Canvas>("SceneCanvas");

    private void OnLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LayerItemViewModel layer } || ViewModel is null || SceneCanvasControl is not { } canvas)
        {
            return;
        }

        ViewModel.RequestLayerSelection(layer);
        _activeLayer = layer;
        _activeHandle = ResizeHandleKind.None;
        _lastCanvasPoint = PreviewCoordinateMapper.ClampToCanvas(e.GetPosition(canvas), ViewModel.CanvasWidth, ViewModel.CanvasHeight);
        _isDragging = !layer.IsLocked;
        _isResizing = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: ResizeHandleKind handle, DataContext: LayerItemViewModel layer }
            || ViewModel is null
            || SceneCanvasControl is not { } canvas)
        {
            return;
        }

        ViewModel.RequestLayerSelection(layer);
        _activeLayer = layer;
        _activeHandle = handle;
        _lastCanvasPoint = PreviewCoordinateMapper.ClampToCanvas(e.GetPosition(canvas), ViewModel.CanvasWidth, ViewModel.CanvasHeight);
        _isDragging = false;
        _isResizing = !layer.IsLocked;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeLayer is null || ViewModel is null || SceneCanvasControl is not { } canvas)
        {
            return;
        }

        var current = PreviewCoordinateMapper.ClampToCanvas(e.GetPosition(canvas), ViewModel.CanvasWidth, ViewModel.CanvasHeight);
        var delta = current - _lastCanvasPoint;
        _lastCanvasPoint = current;

        if (_isDragging)
        {
            ViewModel.MoveLayer(_activeLayer, delta.X, delta.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (_isResizing)
        {
            ViewModel.ResizeLayer(
                _activeLayer,
                _activeHandle,
                delta.X,
                delta.Y,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPointerState(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ClearPointerState(e);
    }

    private void ClearPointerState(RoutedEventArgs e)
    {
        if (_activeLayer is null)
        {
            return;
        }

        _activeLayer = null;
        _activeHandle = ResizeHandleKind.None;
        _isDragging = false;
        _isResizing = false;
        e.Handled = true;
    }
}
