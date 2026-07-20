using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace WTK.MediaForge.Studio.ViewModels;

public enum SettingsTabKind
{
    Interface,
    Language,
    Advanced
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly StudioShellViewModel _shell;
    private SettingsTabKind _selectedTab = SettingsTabKind.Interface;
    private bool _isNavigationVisible = true;
    private bool _isProductionVisible = true;
    private bool _isPropertiesVisible = true;
    private bool _isWorkbenchVisible = true;

    public SettingsViewModel(StudioShellViewModel shell)
    {
        _shell = shell;
        SelectTabCommand = new RelayCommand<SettingsTabKind>(SelectTab);
        RestoreLayoutCommand = shell.RestoreLayoutCommand;
        RedockAllPanelsCommand = shell.RedockAllPanelsCommand;
        RefreshAdvancedSurfaceCommand = new RelayCommand(RefreshAdvancedSurface);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        Tabs =
        [
            new SettingsTabViewModel(SettingsTabKind.Interface, "Interface"),
            new SettingsTabViewModel(SettingsTabKind.Language, "Idioma"),
            new SettingsTabViewModel(SettingsTabKind.Advanced, "Avançado")
        ];
        RefreshAdvancedSurface();
        SelectTab(SettingsTabKind.Interface);
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<SettingsTabViewModel> Tabs { get; }

    public IRelayCommand<SettingsTabKind> SelectTabCommand { get; }

    public ICommand RestoreLayoutCommand { get; }

    public ICommand RedockAllPanelsCommand { get; }

    public ICommand RefreshAdvancedSurfaceCommand { get; }

    public ICommand CloseCommand { get; }

    public ObservableCollection<DiagnosticLogItemViewModel> Diagnostics { get; } = new();

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new();

    public ObservableCollection<OutputMonitorItemViewModel> Outputs { get; } = new();

    public SettingsTabKind SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsInterfaceSelected));
                OnPropertyChanged(nameof(IsLanguageSelected));
                OnPropertyChanged(nameof(IsAdvancedSelected));
            }
        }
    }

    public bool IsInterfaceSelected => SelectedTab == SettingsTabKind.Interface;

    public bool IsLanguageSelected => SelectedTab == SettingsTabKind.Language;

    public bool IsAdvancedSelected => SelectedTab == SettingsTabKind.Advanced;

    public bool IsNavigationVisible
    {
        get => _isNavigationVisible;
        set
        {
            if (SetProperty(ref _isNavigationVisible, value))
            {
                _shell.SetDockToolVisible("tool.navigation", value);
            }
        }
    }

    public bool IsProductionVisible
    {
        get => _isProductionVisible;
        set
        {
            if (SetProperty(ref _isProductionVisible, value))
            {
                _shell.SetDockToolVisible("tool.production", value);
            }
        }
    }

    public bool IsPropertiesVisible
    {
        get => _isPropertiesVisible;
        set
        {
            if (SetProperty(ref _isPropertiesVisible, value))
            {
                _shell.SetDockToolVisible("tool.properties", value);
            }
        }
    }

    public bool IsWorkbenchVisible
    {
        get => _isWorkbenchVisible;
        set
        {
            if (SetProperty(ref _isWorkbenchVisible, value))
            {
                _shell.SetDockToolVisible("tool.workbench", value);
            }
        }
    }

    public string CultureName { get; set; } = "Português (Brasil)";

    public string LanguageRestartHint { get; } = "A alteração de idioma será aplicada na próxima abertura do Studio.";

    private void SelectTab(SettingsTabKind tab)
    {
        SelectedTab = tab;
        if (tab == SettingsTabKind.Advanced)
        {
            RefreshAdvancedSurface();
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = item.Kind == tab;
        }
    }

    private void RefreshAdvancedSurface()
    {
        var snapshot = _shell.CreateAdvancedSurfaceSnapshot();
        Replace(Diagnostics, snapshot.Diagnostics);
        Replace(PerformanceMetrics, snapshot.PerformanceMetrics);
        Replace(Outputs, snapshot.Outputs);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

public sealed record StudioAdvancedSurfaceSnapshot(
    IReadOnlyList<DiagnosticLogItemViewModel> Diagnostics,
    IReadOnlyList<PerformanceMetricViewModel> PerformanceMetrics,
    IReadOnlyList<OutputMonitorItemViewModel> Outputs);

public sealed class SettingsTabViewModel : ViewModelBase
{
    private bool _isSelected;

    public SettingsTabViewModel(SettingsTabKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public SettingsTabKind Kind { get; }

    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
