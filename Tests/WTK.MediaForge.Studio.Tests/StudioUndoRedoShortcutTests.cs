using Avalonia;
using Avalonia.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioUndoRedoServiceTests
{
    [Fact]
    public void Undo_and_redo_restore_independent_scene_snapshots()
    {
        var scene = CreateScene();
        var service = new StudioUndoRedoService();
        service.Reset(scene);

        scene.Layers[0].Transform.X = 240;
        service.Record(scene);

        var undo = service.Undo();
        Assert.Equal(100, undo.Layers[0].Transform.X);
        Assert.True(service.IsCurrentClean);
        Assert.True(service.CanRedo);

        var redo = service.Redo();
        Assert.Equal(240, redo.Layers[0].Transform.X);
        Assert.False(service.IsCurrentClean);
        Assert.True(service.CanUndo);
    }

    [Fact]
    public void Recording_after_undo_drops_redo_tail()
    {
        var scene = CreateScene();
        var service = new StudioUndoRedoService();
        service.Reset(scene);

        scene.Layers[0].Transform.X = 240;
        service.Record(scene);
        _ = service.Undo();

        scene.Layers[0].Transform.X = 320;
        service.Record(scene);

        Assert.False(service.CanRedo);
        Assert.Equal(100, service.Undo().Layers[0].Transform.X);
    }

    private static StudioScene CreateScene()
    {
        var scene = new StudioScene
        {
            Id = "scene-test",
            DisplayName = "Scene"
        };
        var layer = new StudioLayer
        {
            Id = "layer-test",
            Name = "Layer",
            SourceId = "source-test",
            SourceName = "Source",
            Order = 1
        };
        layer.Transform.X = 100;
        layer.Transform.Y = 120;
        layer.Transform.Width = 300;
        layer.Transform.Height = 180;
        scene.Layers.Add(layer);
        return scene;
    }
}

public sealed class StudioShortcutServiceTests
{
    [Theory]
    [InlineData(StudioShortcutKey.Z, true, false, false, StudioShortcutAction.Undo)]
    [InlineData(StudioShortcutKey.Z, true, true, false, StudioShortcutAction.Redo)]
    [InlineData(StudioShortcutKey.Y, true, false, false, StudioShortcutAction.Redo)]
    [InlineData(StudioShortcutKey.S, true, false, false, StudioShortcutAction.SaveProject)]
    [InlineData(StudioShortcutKey.D0, true, false, false, StudioShortcutAction.FitCanvas)]
    [InlineData(StudioShortcutKey.D1, true, false, false, StudioShortcutAction.ActualSize)]
    [InlineData(StudioShortcutKey.Plus, true, false, false, StudioShortcutAction.ZoomIn)]
    [InlineData(StudioShortcutKey.Minus, true, false, false, StudioShortcutAction.ZoomOut)]
    [InlineData(StudioShortcutKey.Z, false, false, false, StudioShortcutAction.None)]
    [InlineData(StudioShortcutKey.Z, true, false, true, StudioShortcutAction.None)]
    public void Resolve_maps_supported_gestures(
        StudioShortcutKey key,
        bool control,
        bool shift,
        bool alt,
        StudioShortcutAction expected)
    {
        var service = new StudioShortcutService();

        var action = service.Resolve(new StudioShortcutGesture(key, control, shift, alt));

        Assert.Equal(expected, action);
    }

    [Fact]
    public void Preview_shortcut_handler_is_a_viewmodel_boundary()
    {
        var preview = new PreviewCanvasViewModel();
        StudioShortcutGesture? observed = null;
        preview.ShortcutHandler = gesture =>
        {
            observed = gesture;
            return true;
        };

        var handled = preview.ExecuteShortcut(new StudioShortcutGesture(StudioShortcutKey.Z, Control: true, Shift: false, Alt: false));

        Assert.True(handled);
        Assert.Equal(StudioShortcutKey.Z, observed?.Key);
    }
}

public sealed class StudioShellUndoRedoShortcutTests
{
    [Fact]
    public void Shortcut_undo_and_redo_restore_scene_draft()
    {
        var shell = CreateShell();
        var layer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);
        shell.SelectLayer(layer);
        var layerId = layer.Id;
        var originalX = layer.X;

        shell.Preview.MoveLayerFromStart(layer, layer.X, layer.Y, new Vector(80, 0), KeyModifiers.None);
        var editedX = shell.BottomWorkbench.Layers.Single(item => item.Id == layerId).X;

        Assert.NotEqual(originalX, editedX);
        Assert.True(shell.UndoCommand.CanExecute(null));

        var undoHandled = shell.ExecuteShortcut(new StudioShortcutGesture(StudioShortcutKey.Z, Control: true, Shift: false, Alt: false));

        Assert.True(undoHandled);
        Assert.False(shell.Preview.HasPendingChanges);
        Assert.False(shell.ApplySceneDraftCommand.CanExecute(null));
        Assert.Equal(originalX, shell.BottomWorkbench.Layers.Single(item => item.Id == layerId).X);

        var redoHandled = shell.ExecuteShortcut(new StudioShortcutGesture(StudioShortcutKey.Z, Control: true, Shift: true, Alt: false));

        Assert.True(redoHandled);
        Assert.True(shell.Preview.HasPendingChanges);
        Assert.Equal(editedX, shell.BottomWorkbench.Layers.Single(item => item.Id == layerId).X);
    }

    [Fact]
    public async Task Selecting_another_scene_resets_undo_history()
    {
        var shell = CreateShell();
        var layer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);

        shell.Preview.MoveLayerFromStart(layer, layer.X, layer.Y, new Vector(80, 0), KeyModifiers.None);
        Assert.True(shell.UndoCommand.CanExecute(null));

        await shell.DiscardSceneDraftCommand.ExecuteAsync(null);
        shell.SelectSceneCard(shell.ProjectExplorer.Scenes.Single(scene => scene.Name == "Interview"));

        Assert.False(shell.UndoCommand.CanExecute(null));
        Assert.False(shell.RedoCommand.CanExecute(null));
    }

    private static StudioShellViewModel CreateShell()
    {
        var services = StudioServiceFactory.CreateFake(uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }
}

public sealed class StudioLayoutPersistenceTests
{
    [Fact]
    public void Layout_service_roundtrips_dock_proportions()
    {
        var path = Path.Combine(Path.GetTempPath(), "WTKMediaForge", $"{Guid.NewGuid():N}", "settings.json");
        var layoutService = new StudioLayoutService(path);
        layoutService.Save(new StudioLayoutDocument
        {
            Layout = new StudioLayoutState
            {
                LeftProportion = 0.17,
                RightProportion = 0.31,
                ProductionProportion = 0.42,
                PropertiesProportion = 0.58,
                BottomProportion = 0.33
            }
        });

        var shell = CreateShell(layoutService);

        Assert.Equal(0.17, FindDock<ToolDock>(shell.DockLayout, "dock.navigation").Proportion, precision: 3);
        Assert.Equal(0.31, FindDock<ProportionalDock>(shell.DockLayout, "dock.right").Proportion, precision: 3);
        Assert.Equal(0.42, FindDock<ToolDock>(shell.DockLayout, "dock.production").Proportion, precision: 3);
        Assert.Equal(0.58, FindDock<ToolDock>(shell.DockLayout, "dock.properties").Proportion, precision: 3);
        Assert.Equal(0.33, FindDock<ToolDock>(shell.DockLayout, "dock.workbench").Proportion, precision: 3);

        FindDock<ToolDock>(shell.DockLayout, "dock.navigation").Proportion = 0.22;
        FindDock<ProportionalDock>(shell.DockLayout, "dock.right").Proportion = 0.28;
        FindDock<ToolDock>(shell.DockLayout, "dock.workbench").Proportion = 0.26;

        shell.PersistLayout();
        var loaded = layoutService.Load();

        Assert.Equal(0.22, loaded.Layout.LeftProportion, precision: 3);
        Assert.Equal(0.28, loaded.Layout.RightProportion, precision: 3);
        Assert.Equal(0.26, loaded.Layout.BottomProportion, precision: 3);
    }

    [Fact]
    public void Layout_service_replaces_invalid_proportions_with_safe_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "WTKMediaForge", $"{Guid.NewGuid():N}", "settings.json");
        var layoutService = new StudioLayoutService(path);
        layoutService.Save(new StudioLayoutDocument
        {
            Layout = new StudioLayoutState
            {
                LeftProportion = 8,
                RightProportion = double.NaN,
                ProductionProportion = -1,
                PropertiesProportion = 0,
                BottomProportion = 2
            }
        });

        var loaded = layoutService.Load();

        Assert.Equal(0.20, loaded.Layout.LeftProportion, precision: 3);
        Assert.Equal(0.25, loaded.Layout.RightProportion, precision: 3);
        Assert.Equal(0.36, loaded.Layout.ProductionProportion, precision: 3);
        Assert.Equal(0.64, loaded.Layout.PropertiesProportion, precision: 3);
        Assert.Equal(0.28, loaded.Layout.BottomProportion, precision: 3);
    }

    private static StudioShellViewModel CreateShell(IStudioLayoutService layoutService)
    {
        var services = StudioServiceFactory.CreateFake(layoutService: layoutService, uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }

    private static T FindDock<T>(IDockable? dockable, string id)
        where T : IDockable
    {
        return EnumerateDockables(dockable).OfType<T>().First(item => item.Id == id);
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable? dockable)
    {
        if (dockable is null)
        {
            yield break;
        }

        yield return dockable;
        if (dockable is not IDock dock || dock.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var descendant in EnumerateDockables(child))
            {
                yield return descendant;
            }
        }
    }
}

public sealed class StudioSettingsAdvancedSurfaceTests
{
    [Fact]
    public void Advanced_surface_contains_diagnostics_performance_and_outputs()
    {
        var shell = CreateShell();
        var settings = new SettingsViewModel(shell);

        settings.SelectTabCommand.Execute(SettingsTabKind.Advanced);

        Assert.True(settings.IsAdvancedSelected);
        Assert.Contains(settings.PerformanceMetrics, item => item.Name == "Frame time");
        Assert.Contains(settings.Outputs, item => item.Name == "Prévia local");
        Assert.NotEmpty(settings.Diagnostics);
    }

    [Fact]
    public void Advanced_surface_refresh_uses_latest_shell_diagnostics()
    {
        var shell = CreateShell();
        var settings = new SettingsViewModel(shell);
        var before = settings.Diagnostics.Count;

        shell.SetDockToolVisible("tool.navigation", false);
        settings.RefreshAdvancedSurfaceCommand.Execute(null);

        Assert.True(settings.Diagnostics.Count > before);
        Assert.Contains(settings.Diagnostics, item => item.Message.Contains("oculto", StringComparison.OrdinalIgnoreCase));
    }

    private static StudioShellViewModel CreateShell()
    {
        var path = Path.Combine(Path.GetTempPath(), "WTKMediaForge", $"{Guid.NewGuid():N}", "settings.json");
        var services = StudioServiceFactory.CreateFake(
            layoutService: new StudioLayoutService(path),
            uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }
}
