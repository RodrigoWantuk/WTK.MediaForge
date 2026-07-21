using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private ResizeHandleKind _hoverHandle = ResizeHandleKind.None;
    private SceneEditorInteractionMode _interactionMode = SceneEditorInteractionMode.None;
    private Point _dragStartViewport;
    private Point _dragStartScene;
    private double _layerStartX;
    private double _layerStartY;
    private Rect _resizeStartBounds;
    private bool _isSpaceDown;
    private LayerItemViewModel? _hoverLayer;
    private bool _isHoveringVisibilityToggle;

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

        if (point.Properties.IsRightButtonPressed)
        {
            var contextLayer = SceneEditorHitTest.HitTestLayer(vm.Layers, _dragStartScene);
            vm.RequestLayerSelection(contextLayer);
            ShowContextMenu(vm, contextLayer);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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
            var selectedCorners = SceneEditorHitTest.LayerViewportCorners(selectedLayer, vm.Transform);
            if (SceneEditorHitTest.HitTestVisibilityToggle(selectedCorners, _dragStartViewport))
            {
                vm.ToggleLayerVisibility(selectedLayer);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var handle = SceneEditorHitTest.HitTestResizeHandle(selectedCorners, _dragStartViewport, HandleSize + 8);
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
            Cursor = new Cursor(StandardCursorType.Hand);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_interactionMode == SceneEditorInteractionMode.None)
        {
            UpdateHover(vm, viewportPoint);
        }

        if (_activeLayer is null)
        {
            return;
        }

        var scenePoint = vm.ScreenToScene(viewportPoint);
        var sceneDelta = scenePoint - _dragStartScene;
        if (_interactionMode == SceneEditorInteractionMode.MoveLayer)
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
            vm.MoveLayerFromStart(_activeLayer, _layerStartX, _layerStartY, sceneDelta, e.KeyModifiers);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_interactionMode == SceneEditorInteractionMode.ResizeLayer)
        {
            Cursor = CursorForHandle(_activeHandle);
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

        if (AvaloniaStudioShortcutMapper.TryCreate(e, out var gesture) && vm.ExecuteShortcut(gesture))
        {
            InvalidateVisual();
            e.Handled = true;
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
        Cursor = mode switch
        {
            SceneEditorInteractionMode.Pan => new Cursor(StandardCursorType.Hand),
            SceneEditorInteractionMode.MoveLayer => new Cursor(StandardCursorType.SizeAll),
            SceneEditorInteractionMode.ResizeLayer => CursorForHandle(handle),
            _ => new Cursor(StandardCursorType.Arrow)
        };
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void ClearInteraction(PointerEventArgs e)
    {
        _interactionMode = SceneEditorInteractionMode.None;
        _activeLayer = null;
        _activeHandle = ResizeHandleKind.None;
        Cursor = new Cursor(StandardCursorType.Arrow);
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
            var isHover = ReferenceEquals(layer, _hoverLayer);
            var fill = layer.IsSelected ? Brush("MfSelectedBrush", Brushes.DarkSlateBlue) : Brush("MfOverlayBrush", Brushes.Black);
            var border = layer.IsSelected ? Pen("MfAccentBrush", 2) : isHover ? Pen("MfAccentBrush", 1.5) : Pen("MfBorderNormalBrush", 1);

            if (Math.Abs(layer.RotationDegrees) < double.Epsilon)
            {
                DrawLayerContent(context, layer, rect, fill, border);
            }
            else
            {
                var pivot = vm.Transform.SceneToViewport(new Point(
                    layer.X + layer.Width * layer.PivotX,
                    layer.Y + layer.Height * layer.PivotY));
                using (context.PushTransform(Matrix.CreateRotation(layer.RotationDegrees * Math.PI / 180d, pivot)))
                {
                    DrawLayerContent(context, layer, rect, fill, border);
                }
            }
        }

        if (layer.IsSelected)
        {
            DrawSelection(context, SceneEditorHitTest.LayerViewportCorners(layer, vm.Transform), layer.IsLocked, layer.IsVisible);
        }
    }

    private void DrawLayerContent(DrawingContext context, LayerItemViewModel layer, Rect rect, IBrush fill, Pen border)
    {
        context.DrawRectangle(fill, border, rect, 8, 8);
        DrawText(context, layer.Name, rect.TopLeft + new Vector(14, 12), 14, Brush("MfTextPrimaryBrush", Brushes.White), FontWeight.SemiBold);
        DrawText(context, layer.Source, rect.TopLeft + new Vector(14, 32), 12, Brush("MfTextSecondaryBrush", Brushes.LightGray), FontWeight.Normal);
    }

    private void DrawSelection(DrawingContext context, IReadOnlyList<Point> corners, bool isLocked, bool isVisible)
    {
        if (corners.Count < 4)
        {
            return;
        }

        var pen = Pen(isLocked ? "MfWarningBrush" : "MfAccentBrush", 2);
        for (var i = 0; i < corners.Count; i++)
        {
            context.DrawLine(pen, corners[i], corners[(i + 1) % corners.Count]);
        }

        foreach (var handle in SceneEditorHitTest.HandleRects(corners, HandleSize))
        {
            context.DrawRectangle(Brush("MfAccentBrush", Brushes.DeepSkyBlue), Pen("MfCanvasOutsideBrush", 1), handle.Rect, 2, 2);
        }

        DrawVisibilityToggle(context, SceneEditorHitTest.VisibilityToggleRect(corners), isVisible);
    }

    private void DrawVisibilityToggle(DrawingContext context, Rect rect, bool isVisible)
    {
        var background = _isHoveringVisibilityToggle
            ? Brush("MfAccentBrush", Brushes.DeepSkyBlue)
            : Brush("MfSurface2Brush", Brushes.Black);
        context.DrawRectangle(background, Pen("MfCanvasOutsideBrush", 1), rect, 6, 6);

        var center = rect.Center;
        var eyeRect = new Rect(center.X - 7, center.Y - 4, 14, 8);
        context.DrawEllipse(null, Pen("MfTextPrimaryBrush", 1.4), eyeRect);
        context.DrawEllipse(Brush("MfTextPrimaryBrush", Brushes.White), null, new Rect(center.X - 2, center.Y - 2, 4, 4));
        if (!isVisible)
        {
            context.DrawLine(Pen("MfWarningBrush", 1.6), rect.TopLeft + new Vector(5, 5), rect.BottomRight - new Vector(5, 5));
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
        if (vm.SafeAreaMode == SafeAreaDisplayMode.Hidden)
        {
            return;
        }

        var margin = vm.SafeAreaMarginPercent / 100d;
        var safe = new Rect(vm.CanvasWidth * margin, vm.CanvasHeight * margin, vm.CanvasWidth * (1 - margin * 2), vm.CanvasHeight * (1 - margin * 2));
        var viewportRect = vm.Transform.SceneToViewport(safe);
        context.DrawRectangle(null, Pen("MfWarningBrush", 1), viewportRect);
        DrawText(
            context,
            $"{vm.SafeAreaModeLabel} • {vm.SafeAreaProfileLabel}",
            viewportRect.TopLeft + new Vector(8, 8),
            12,
            Brush("MfWarningBrush", Brushes.Gold),
            FontWeight.SemiBold);
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

    private void UpdateHover(PreviewCanvasViewModel vm, Point viewportPoint)
    {
        var previousLayer = _hoverLayer;
        var previousHandle = _hoverHandle;
        var previousToggle = _isHoveringVisibilityToggle;

        _hoverLayer = null;
        _hoverHandle = ResizeHandleKind.None;
        _isHoveringVisibilityToggle = false;

        if (_isSpaceDown)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        var selectedLayer = vm.SelectedLayer;
        if (selectedLayer is not null && selectedLayer.IsVisible)
        {
            var selectedCorners = SceneEditorHitTest.LayerViewportCorners(selectedLayer, vm.Transform);
            _isHoveringVisibilityToggle = SceneEditorHitTest.HitTestVisibilityToggle(selectedCorners, viewportPoint);
            if (_isHoveringVisibilityToggle)
            {
                _hoverLayer = selectedLayer;
                Cursor = new Cursor(StandardCursorType.Hand);
                InvalidateHoverIfChanged(previousLayer, previousHandle, previousToggle);
                return;
            }

            _hoverHandle = SceneEditorHitTest.HitTestResizeHandle(selectedCorners, viewportPoint, HandleSize + 8);
            if (_hoverHandle != ResizeHandleKind.None)
            {
                _hoverLayer = selectedLayer;
                Cursor = CursorForHandle(_hoverHandle);
                InvalidateHoverIfChanged(previousLayer, previousHandle, previousToggle);
                return;
            }
        }

        var scenePoint = vm.ScreenToScene(viewportPoint);
        _hoverLayer = SceneEditorHitTest.HitTestLayer(vm.Layers, scenePoint);
        Cursor = _hoverLayer is null
            ? new Cursor(StandardCursorType.Arrow)
            : new Cursor(StandardCursorType.Hand);
        InvalidateHoverIfChanged(previousLayer, previousHandle, previousToggle);
    }

    private void InvalidateHoverIfChanged(LayerItemViewModel? previousLayer, ResizeHandleKind previousHandle, bool previousToggle)
    {
        if (!ReferenceEquals(previousLayer, _hoverLayer)
            || previousHandle != _hoverHandle
            || previousToggle != _isHoveringVisibilityToggle)
        {
            InvalidateVisual();
        }
    }

    private static Cursor CursorForHandle(ResizeHandleKind handle)
    {
        return handle switch
        {
            ResizeHandleKind.Left or ResizeHandleKind.Right => new Cursor(StandardCursorType.SizeWestEast),
            ResizeHandleKind.Top or ResizeHandleKind.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
            ResizeHandleKind.TopLeft or ResizeHandleKind.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
            ResizeHandleKind.TopRight or ResizeHandleKind.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }

    private void ShowContextMenu(PreviewCanvasViewModel vm, LayerItemViewModel? layer)
    {
        var items = layer is null
            ? CreateCanvasContextMenuItems(vm)
            : CreateLayerContextMenuItems(vm, layer);

        var menu = new ContextMenu
        {
            ItemsSource = items
        };
        menu.Open(this);
    }

    private IEnumerable<object> CreateLayerContextMenuItems(PreviewCanvasViewModel vm, LayerItemViewModel layer)
    {
        yield return MenuItem(layer.IsVisible ? "Ocultar camada" : "Mostrar camada", () => vm.ToggleLayerVisibility(layer));
        yield return MenuItem(layer.IsLocked ? "Desbloquear camada" : "Bloquear camada", () => vm.ToggleLayerLock(layer));
        yield return new Separator();
        yield return MenuItem("Trazer para frente", () => vm.BringLayerToFront(layer));
        yield return MenuItem("Enviar para trás", () => vm.SendLayerToBack(layer));
        yield return MenuItem("Redefinir transformação", () => vm.ResetLayerTransform(layer));
        yield return new Separator();
        yield return MenuItem("Abrir propriedades", () => vm.RequestLayerSelection(layer));
    }

    private IEnumerable<object> CreateCanvasContextMenuItems(PreviewCanvasViewModel vm)
    {
        yield return MenuItem("Adicionar entrada à cena", () => vm.AddSourceCommand?.Execute(null), vm.AddSourceCommand?.CanExecute(null) == true);
        yield return MenuItem("Colar", static () => { }, isEnabled: false);
        yield return new Separator();
        yield return MenuItem("Ajustar à tela", vm.FitZoom);
        yield return MenuItem(vm.IsGridVisible ? "Ocultar grade" : "Mostrar grade", () => vm.ToggleGridCommand.Execute(null));
        yield return MenuItem(vm.IsSafeFrameVisible ? "Ocultar área segura" : "Mostrar área segura", () => vm.ToggleSafeFrameCommand.Execute(null));
    }

    private MenuItem MenuItem(string header, Action action, bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled
        };
        item.Click += (_, e) =>
        {
            action();
            InvalidateVisual();
            e.Handled = true;
        };
        return item;
    }
}
