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
    private SceneEditorInteractionMode _interactionMode = SceneEditorInteractionMode.None;
    private Point _lastScenePoint;
    private Point _lastScreenPoint;
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

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not ResizeHandleControl handle || handle.DataContext is not LayerItemViewModel layer)
        {
            return;
        }

        Focus();
        vm.RequestLayerSelection(layer);
        if (layer.IsLocked)
        {
            return;
        }

        _activeLayer = layer;
        _activeHandle = handle.HandleKind;
        _interactionMode = SceneEditorInteractionMode.ResizeLayer;
        _lastScreenPoint = e.GetCurrentPoint(this).Position;
        _lastScenePoint = vm.ScreenToScene(_lastScreenPoint);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || e.Handled)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        _lastScreenPoint = point.Position;
        _lastScenePoint = vm.ScreenToScene(point.Position);

        if (point.Properties.IsMiddleButtonPressed || _isSpaceDown)
        {
            _interactionMode = SceneEditorInteractionMode.Pan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var layer = vm.HitTest(_lastScenePoint);
        if (layer is null)
        {
            vm.RequestLayerSelection(null);
            ClearPointerState(e);
            return;
        }

        vm.RequestLayerSelection(layer);
        _activeLayer = layer;
        _activeHandle = ResizeHandleKind.None;
        _interactionMode = layer.IsLocked ? SceneEditorInteractionMode.None : SceneEditorInteractionMode.MoveLayer;
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
        if (_interactionMode == SceneEditorInteractionMode.Pan)
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

        var scenePoint = vm.ScreenToScene(screen);
        var deltaScene = scenePoint - _lastScenePoint;
        _lastScenePoint = scenePoint;

        if (_interactionMode == SceneEditorInteractionMode.MoveLayer)
        {
            vm.MoveLayer(_activeLayer, deltaScene.X, deltaScene.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (_interactionMode == SceneEditorInteractionMode.ResizeLayer)
        {
            vm.ResizeLayer(
                _activeLayer,
                _activeHandle,
                deltaScene.X,
                deltaScene.Y,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Alt));
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

        vm.ZoomAtScreenPoint(e.GetPosition(this), e.Delta.Y > 0 ? 1.12 : 1 / 1.12);
        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (AvaloniaStudioShortcutMapper.TryCreate(e, out var gesture) && vm.ExecuteShortcut(gesture))
        {
            e.Handled = true;
            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case Key.Space:
                _isSpaceDown = true;
                break;
            case Key.Escape:
                vm.RequestLayerSelection(null);
                break;
            case Key.D0 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.FitZoom();
                break;
            case Key.D1 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.SetActualSizeAtCenter();
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

    private void ClearPointerState(RoutedEventArgs e)
    {
        _activeLayer = null;
        _activeHandle = ResizeHandleKind.None;
        _interactionMode = SceneEditorInteractionMode.None;
        if (e is PointerEventArgs pointerEvent)
        {
            pointerEvent.Pointer.Capture(null);
        }

        e.Handled = true;
    }
}
