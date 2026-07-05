using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed class StudioSceneEditorControl : Control
{
    private const double HandleSize = 10;
    private PreviewCanvasViewModel? _viewModel;
    private LayerItemViewModel? _activeLayer;
    private ResizeHandleKind _activeHandle = ResizeHandleKind.None;
    private SceneEditorInteractionMode _interactionMode = SceneEditorInteractionMode.None;
    private Point _dragStartViewport;
    private Point _dragStartScene;
    private double _layerStartX;
    private double _layerStartY;
    private Rect _resizeStartBounds;
    private bool _isSpaceDown;

    public StudioSceneEditorControl()
    {
        Focusable = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        DetachViewModel();
        base.OnDataContextChanged(e);
        _viewModel = DataContext as PreviewCanvasViewModel;
        AttachViewModel();
        _viewModel?.SetViewport(Bounds.Width, Bounds.Height);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _viewModel?.SetViewport(e.NewSize.Width, e.NewSize.Height);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = _viewModel;
        if (vm is null)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Brush("MfCanvasOutsideBrush", Brushes.Black), null, bounds);
        DrawCheckerboard(context, bounds);

        var canvasRect = vm.Transform.SceneToViewport(new Rect(0, 0, vm.CanvasWidth, vm.CanvasHeight));
        context.DrawRectangle(Brush("MfCanvasSurfaceBrush", Brushes.Black), Pen("MfBorderStrongBrush", 1), canvasRect, 2, 2);

        using (context.PushClip(canvasRect))
        {
            DrawPreviewBackdrop(context, canvasRect);
            if (vm.IsGridVisible)
            {
                DrawGrid(context, vm, canvasRect);
            }

            foreach (var layer in vm.Layers.Where(item => item.IsVisible).OrderBy(item => item.Order))
            {
                DrawLayer(context, vm, layer);
            }

            if (vm.IsSafeFrameVisible)
            {
                DrawSafeArea(context, vm);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        _dragStartViewport = point.Position;
        _dragStartScene = vm.ScreenToScene(_dragStartViewport);

        if (point.Properties.IsMiddleButtonPressed || (_isSpaceDown && point.Properties.IsLeftButtonPressed))
        {
            BeginInteraction(SceneEditorInteractionMode.Pan, null, ResizeHandleKind.None, e);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var selectedLayer = vm.SelectedLayer;
        if (selectedLayer is not null)
        {
            var selectedRect = vm.Transform.SceneToViewport(SceneEditorHitTest.LayerSceneRect(selectedLayer));
            var handle = SceneEditorHitTest.HitTestResizeHandle(selectedRect, _dragStartViewport, HandleSize + 8);
            if (handle != ResizeHandleKind.None)
            {
                vm.RequestLayerSelection(selectedLayer);
                if (!selectedLayer.IsLocked)
                {
                    _resizeStartBounds = SceneEditorHitTest.LayerSceneRect(selectedLayer);
                    BeginInteraction(SceneEditorInteractionMode.ResizeLayer, selectedLayer, handle, e);
                }

                e.Handled = true;
                return;
            }
        }

        var layer = SceneEditorHitTest.HitTestLayer(vm.Layers, _dragStartScene);
        if (layer is null)
        {
            vm.RequestLayerSelection(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        vm.RequestLayerSelection(layer);
        if (!layer.IsLocked)
        {
            _layerStartX = layer.X;
            _layerStartY = layer.Y;
            BeginInteraction(SceneEditorInteractionMode.MoveLayer, layer, ResizeHandleKind.None, e);
        }
        else
        {
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        var viewportPoint = e.GetCurrentPoint(this).Position;
        if (_interactionMode == SceneEditorInteractionMode.Pan)
        {
            var delta = viewportPoint - _dragStartViewport;
            _dragStartViewport = viewportPoint;
            vm.PanBy(delta.X, delta.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_activeLayer is null)
        {
            return;
        }

        var scenePoint = vm.ScreenToScene(viewportPoint);
        var sceneDelta = scenePoint - _dragStartScene;
        if (_interactionMode == SceneEditorInteractionMode.MoveLayer)
        {
            vm.MoveLayerFromStart(_activeLayer, _layerStartX, _layerStartY, sceneDelta, e.KeyModifiers);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_interactionMode == SceneEditorInteractionMode.ResizeLayer)
        {
            vm.ResizeLayerFromStart(_activeLayer, _activeHandle, _resizeStartBounds, sceneDelta, e.KeyModifiers);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ClearInteraction(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        vm.ZoomAtScreenPoint(e.GetPosition(this), e.Delta.Y > 0 ? 1.12 : 1 / 1.12);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _isSpaceDown = true;
                e.Handled = true;
                break;
            case Key.Escape:
                vm.RequestLayerSelection(null);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.D0 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.FitZoom();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.D1 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.SetActualSizeAtCenter();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Left:
                vm.NudgeSelectedLayer(-1, 0, e.KeyModifiers);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Right:
                vm.NudgeSelectedLayer(1, 0, e.KeyModifiers);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Up:
                vm.NudgeSelectedLayer(0, -1, e.KeyModifiers);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Down:
                vm.NudgeSelectedLayer(0, 1, e.KeyModifiers);
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _isSpaceDown = false;
            e.Handled = true;
        }
    }

    private void BeginInteraction(SceneEditorInteractionMode mode, LayerItemViewModel? layer, ResizeHandleKind handle, PointerEventArgs e)
    {
        _interactionMode = mode;
        _activeLayer = layer;
        _activeHandle = handle;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void ClearInteraction(PointerEventArgs e)
    {
        _interactionMode = SceneEditorInteractionMode.None;
        _activeLayer = null;
        _activeHandle = ResizeHandleKind.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void AttachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Layers.CollectionChanged += OnLayersCollectionChanged;
        foreach (var layer in _viewModel.Layers)
        {
            layer.PropertyChanged += OnLayerPropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Layers.CollectionChanged -= OnLayersCollectionChanged;
        foreach (var layer in _viewModel.Layers)
        {
            layer.PropertyChanged -= OnLayerPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (LayerItemViewModel layer in e.OldItems)
            {
                layer.PropertyChanged -= OnLayerPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (LayerItemViewModel layer in e.NewItems)
            {
                layer.PropertyChanged += OnLayerPropertyChanged;
            }
        }

        InvalidateVisual();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void DrawLayer(DrawingContext context, PreviewCanvasViewModel vm, LayerItemViewModel layer)
    {
        var rect = vm.Transform.SceneToViewport(SceneEditorHitTest.LayerSceneRect(layer));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using (context.PushOpacity(layer.LayerOpacity))
        {
            var fill = layer.IsSelected ? Brush("MfSelectedBrush", Brushes.DarkSlateBlue) : Brush("MfOverlayBrush", Brushes.Black);
            var border = layer.IsSelected ? Pen("MfAccentBrush", 2) : Pen("MfBorderNormalBrush", 1);
            context.DrawRectangle(fill, border, rect, 8, 8);
            DrawText(context, layer.Name, rect.TopLeft + new Vector(14, 12), 14, Brush("MfTextPrimaryBrush", Brushes.White), FontWeight.SemiBold);
            DrawText(context, layer.Source, rect.TopLeft + new Vector(14, 32), 12, Brush("MfTextSecondaryBrush", Brushes.LightGray), FontWeight.Normal);
        }

        if (layer.IsSelected)
        {
            DrawSelection(context, rect, layer.IsLocked);
        }
    }

    private void DrawSelection(DrawingContext context, Rect rect, bool isLocked)
    {
        context.DrawRectangle(null, Pen(isLocked ? "MfWarningBrush" : "MfAccentBrush", 2), rect, 8, 8);
        foreach (var handle in SceneEditorHitTest.HandleRects(rect, HandleSize))
        {
            context.DrawRectangle(Brush("MfAccentBrush", Brushes.DeepSkyBlue), Pen("MfCanvasOutsideBrush", 1), handle.Rect, 2, 2);
        }
    }

    private void DrawGrid(DrawingContext context, PreviewCanvasViewModel vm, Rect canvasRect)
    {
        var minor = vm.Zoom >= 0.75 ? 10 : vm.Zoom >= 0.35 ? 20 : 0;
        var minorPen = Pen("MfBorderSubtleBrush", 0.6);
        var majorPen = Pen("MfBorderNormalBrush", 1);

        if (minor > 0)
        {
            DrawGridLines(context, vm, minor, minorPen);
        }

        DrawGridLines(context, vm, vm.Snap.MajorGridSize, majorPen);
        context.DrawRectangle(null, Pen("MfBorderStrongBrush", 1), canvasRect, 2, 2);
    }

    private void DrawGridLines(DrawingContext context, PreviewCanvasViewModel vm, double step, Pen pen)
    {
        for (var x = step; x < vm.CanvasWidth; x += step)
        {
            var a = vm.Transform.SceneToViewport(new Point(x, 0));
            var b = vm.Transform.SceneToViewport(new Point(x, vm.CanvasHeight));
            context.DrawLine(pen, a, b);
        }

        for (var y = step; y < vm.CanvasHeight; y += step)
        {
            var a = vm.Transform.SceneToViewport(new Point(0, y));
            var b = vm.Transform.SceneToViewport(new Point(vm.CanvasWidth, y));
            context.DrawLine(pen, a, b);
        }
    }

    private void DrawSafeArea(DrawingContext context, PreviewCanvasViewModel vm)
    {
        var safe = new Rect(vm.CanvasWidth * 0.05, vm.CanvasHeight * 0.05, vm.CanvasWidth * 0.9, vm.CanvasHeight * 0.9);
        context.DrawRectangle(null, Pen("MfWarningBrush", 1), vm.Transform.SceneToViewport(safe));
    }

    private void DrawPreviewBackdrop(DrawingContext context, Rect canvasRect)
    {
        var top = new Rect(canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height * 0.18);
        var bottom = new Rect(canvasRect.X, canvasRect.Bottom - canvasRect.Height * 0.18, canvasRect.Width, canvasRect.Height * 0.18);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(92, 0, 174, 239)), null, top);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 233, 30, 99)), null, bottom);
    }

    private void DrawCheckerboard(DrawingContext context, Rect bounds)
    {
        const double size = 24;
        var a = Brush("MfCheckerA", Brushes.DimGray);
        var b = Brush("MfCheckerB", Brushes.Black);
        for (var y = 0d; y < bounds.Height; y += size)
        {
            for (var x = 0d; x < bounds.Width; x += size)
            {
                var useA = ((int)(x / size) + (int)(y / size)) % 2 == 0;
                context.DrawRectangle(useA ? a : b, null, new Rect(x, y, size, size));
            }
        }
    }

    private void DrawText(DrawingContext context, string text, Point origin, double fontSize, IBrush brush, FontWeight weight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            fontSize,
            brush);
        context.DrawText(formatted, origin);
    }

    private IBrush Brush(string key, IBrush fallback)
    {
        return Application.Current is { } app
            && app.TryGetResource(key, ActualThemeVariant, out var value)
            && value is IBrush brush
                ? brush
                : fallback;
    }

    private Pen Pen(string key, double thickness)
    {
        return new Pen(Brush(key, Brushes.White), thickness);
    }
}
