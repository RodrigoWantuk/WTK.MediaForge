using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using WTK.MediaForge.Studio.ViewModels;
using Alignment = Dock.Model.Core.Alignment;
using Orientation = Dock.Model.Core.Orientation;

namespace WTK.MediaForge.Studio.Docking;

public sealed class StudioDockFactory : Factory
{
    private readonly StudioShellViewModel _shell;

    public StudioDockFactory(StudioShellViewModel shell)
    {
        _shell = shell;
    }

    public override IRootDock CreateLayout()
    {
        var navigationDock = CreateToolDock(
            "dock.navigation",
            Alignment.Left,
            _shell.NavigationLayoutProportion,
            CreateTool("tool.navigation", "Navegação", _shell.ProjectExplorer, minWidth: 240));

        var productionDock = CreateToolDock(
            "dock.production",
            Alignment.Right,
            _shell.ProductionLayoutProportion,
            CreateTool("tool.production", "Produção", _shell.Production, minWidth: 320, minHeight: 160));

        var propertiesDock = CreateToolDock(
            "dock.properties",
            Alignment.Right,
            _shell.PropertiesLayoutProportion,
            CreateTool("tool.properties", "Propriedades", _shell.Inspector, minWidth: 340));

        var bottomDock = CreateToolDock(
            "dock.workbench",
            Alignment.Bottom,
            _shell.WorkbenchLayoutProportion,
            CreateTool("tool.workbench", "Camadas e saídas", _shell.BottomWorkbench, minHeight: 180));

        var editor = new Document
        {
            Id = "document.scene-editor",
            Title = "Editor da cena",
            Context = _shell.PreviewWorkspace,
            CanClose = false,
            CanFloat = false,
            CanDrag = false
        };

        var editorDock = new DocumentDock
        {
            Id = "dock.documents",
            Title = "Editor",
            CanCloseLastDockable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>(editor),
            ActiveDockable = editor,
            DefaultDockable = editor
        };

        var rightDock = new ProportionalDock
        {
            Id = "dock.right",
            Orientation = Orientation.Vertical,
            Proportion = _shell.RightLayoutProportion,
            VisibleDockables = CreateList<IDockable>(
                productionDock,
                new ProportionalDockSplitter(),
                propertiesDock),
            ActiveDockable = propertiesDock
        };

        var centerDock = new ProportionalDock
        {
            Id = "dock.center",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                editorDock,
                new ProportionalDockSplitter(),
                bottomDock),
            ActiveDockable = editorDock
        };

        var mainDock = new ProportionalDock
        {
            Id = "dock.main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                navigationDock,
                new ProportionalDockSplitter(),
                centerDock,
                new ProportionalDockSplitter(),
                rightDock),
            ActiveDockable = centerDock
        };

        var root = CreateRootDock();

        root.Id = "dock.root";
        root.Title = "WTK MediaForge Studio";
        root.VisibleDockables = CreateList<IDockable>(mainDock);
        root.ActiveDockable = mainDock;
        root.DefaultDockable = mainDock;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => DockSettings.UseManagedWindows
                ? new ManagedHostWindow()
                : new HostWindow()
        };

        base.InitLayout(layout);
    }

    private ToolDock CreateToolDock(string id, Alignment alignment, double proportion, Tool tool)
    {
        return new ToolDock
        {
            Id = id,
            Title = tool.Title,
            Alignment = alignment,
            Proportion = proportion,
            VisibleDockables = CreateList<IDockable>(tool),
            ActiveDockable = tool,
            DefaultDockable = tool
        };
    }

    private static Tool CreateTool(
        string id,
        string title,
        object context,
        double minWidth = 0,
        double minHeight = 0)
    {
        return new Tool
        {
            Id = id,
            Title = title,
            Context = context,
            CanClose = false,
            CanFloat = true,
            CanPin = true,
            CanDrag = true,
            CanDrop = true,
            MinWidth = minWidth,
            MinHeight = minHeight
        };
    }
}
