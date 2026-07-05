using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed partial class StudioCanvasEditor : UserControl
{
    private LayerItemViewModel? _activeLayer;
    private ResizeHandleKind _activeHandle = ResizeHandleKind.None;
    private Point _lastCanvasPoint;
    private Point _lastScreenPoint;
    private bool _isDragging;
    private bool _isResizing;
    private bool _isPanning;
    private bool _isSpaceDown;

    public StudioCanvasEditor()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private PreviewCanvasViewModel? ViewModel => DataContext as PreviewCanvasViewModel;

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ViewModel?.SetViewport(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        _lastScreenPoint = point.Position;
        _lastCanvasPoint = ToCanvas(point.Position, vm);

        if (point.Properties.IsMiddleButtonPressed || _isSpaceDown)
        {
            _isPanning = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var layer = vm.HitTest(_lastCanvasPoint.X, _lastCanvasPoint.Y);
        if (layer is null)
        {
            vm.SelectLayerFromOwner(null);
            ClearPointerState(e);
            return;
        }

        vm.RequestLayerSelection(layer);
        _activeLayer = layer;
        _activeHandle = DetectHandle(layer, _lastCanvasPoint, vm.Zoom);
        _isResizing = _activeHandle != ResizeHandleKind.None && !layer.IsLocked;
        _isDragging = !_isResizing && !layer.IsLocked;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var screen = e.GetCurrentPoint(this).Position;
        if (_isPanning)
        {
            var delta = screen - _lastScreenPoint;
            _lastScreenPoint = screen;
            vm.PanBy(delta.X, delta.Y);
            e.Handled = true;
            return;
        }

        if (_activeLayer is null)
        {
            return;
        }

        var current = ToCanvas(screen, vm);
        var deltaCanvas = current - _lastCanvasPoint;
        _lastCanvasPoint = current;

        if (_isDragging)
        {
            vm.MoveLayer(_activeLayer, deltaCanvas.X, deltaCanvas.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (_isResizing)
        {
            vm.ResizeLayer(
                _activeLayer,
                _activeHandle,
                deltaCanvas.X,
                deltaCanvas.Y,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        }
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPointerState(e);
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.ZoomAtCenter(e.Delta.Y > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case Key.Space:
                _isSpaceDown = true;
                break;
            case Key.Left:
                vm.NudgeSelectedLayer(-1, 0, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Right:
                vm.NudgeSelectedLayer(1, 0, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Up:
                vm.NudgeSelectedLayer(0, -1, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Down:
                vm.NudgeSelectedLayer(0, 1, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Delete:
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    private void OnEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _isSpaceDown = false;
            e.Handled = true;
        }
    }

    private static Point ToCanvas(Point screenPoint, PreviewCanvasViewModel vm)
    {
        return new Point((screenPoint.X - vm.PanX) / vm.Zoom, (screenPoint.Y - vm.PanY) / vm.Zoom);
    }

    private static ResizeHandleKind DetectHandle(LayerItemViewModel layer, Point canvasPoint, double zoom)
    {
        var tolerance = Math.Max(6, 14 / Math.Max(0.1, zoom));
        var nearLeft = Math.Abs(canvasPoint.X - layer.X) <= tolerance;
        var nearRight = Math.Abs(canvasPoint.X - (layer.X + layer.Width)) <= tolerance;
        var nearTop = Math.Abs(canvasPoint.Y - layer.Y) <= tolerance;
        var nearBottom = Math.Abs(canvasPoint.Y - (layer.Y + layer.Height)) <= tolerance;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => ResizeHandleKind.TopLeft,
            (_, true, true, _) => ResizeHandleKind.TopRight,
            (true, _, _, true) => ResizeHandleKind.BottomLeft,
            (_, true, _, true) => ResizeHandleKind.BottomRight,
            (true, _, _, _) => ResizeHandleKind.Left,
            (_, true, _, _) => ResizeHandleKind.Right,
            (_, _, true, _) => ResizeHandleKind.Top,
            (_, _, _, true) => ResizeHandleKind.Bottom,
            _ => ResizeHandleKind.None
        };
    }

    private void ClearPointerState(RoutedEventArgs e)
    {
        _activeLayer = null;
        _activeHandle = ResizeHandleKind.None;
        _isDragging = false;
        _isResizing = false;
        _isPanning = false;
        e.Handled = true;
    }
}
