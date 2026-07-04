using System.Collections.ObjectModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class ProjectTreeGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public ProjectTreeGroupViewModel(string title, IEnumerable<ProjectTreeItemViewModel> items)
    {
        Title = title;
        Items = new ObservableCollection<ProjectTreeItemViewModel>(items);
    }

    public string Title { get; }

    public ObservableCollection<ProjectTreeItemViewModel> Items { get; }

    public int Count => Items.Count;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed class ProjectTreeItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isActive;

    public ProjectTreeItemViewModel(
        StudioProjectItemKind kind,
        string name,
        string metadata,
        string icon,
        string badge = "",
        string health = "OK")
    {
        Kind = kind;
        Name = name;
        Metadata = metadata;
        Icon = icon;
        Badge = badge;
        Health = health;
    }

    public StudioProjectItemKind Kind { get; }

    public string Name { get; }

    public string Metadata { get; }

    public string Icon { get; }

    public string Badge { get; }

    public string Health { get; }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public ICommand? SelectCommand { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

public sealed class LayerItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isLocked;

    public LayerItemViewModel(string name, string source, string type, string icon, int order)
    {
        Name = name;
        Source = source;
        Type = type;
        Icon = icon;
        Order = order;
    }

    public string Name { get; }

    public string Source { get; }

    public string Type { get; }

    public string Icon { get; }

    public int Order { get; }

    public ICommand? SelectCommand { get; set; }

    public ICommand? ToggleVisibilityCommand { get; set; }

    public ICommand? ToggleLockCommand { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }
}

public sealed class EffectItemViewModel : ViewModelBase
{
    private bool _isEnabled;
    private bool _isExpanded;

    public EffectItemViewModel(string name, string description, bool isEnabled, bool isExpanded)
    {
        Name = name;
        Description = description;
        _isEnabled = isEnabled;
        _isExpanded = isExpanded;
    }

    public string Name { get; }

    public string Description { get; }

    public ICommand? ToggleEnabledCommand { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed class DiagnosticLogItemViewModel
{
    public DiagnosticLogItemViewModel(string time, string level, string message)
    {
        Time = time;
        Level = level;
        Message = message;
    }

    public string Time { get; }

    public string Level { get; }

    public string Message { get; }
}

public sealed class PerformanceMetricViewModel
{
    public PerformanceMetricViewModel(string name, string value, string detail)
    {
        Name = name;
        Value = value;
        Detail = detail;
    }

    public string Name { get; }

    public string Value { get; }

    public string Detail { get; }
}

public sealed class OutputMonitorItemViewModel
{
    public OutputMonitorItemViewModel(string name, StudioOutputState state, string destination, string bitrate, string health)
    {
        Name = name;
        State = state;
        Destination = destination;
        Bitrate = bitrate;
        Health = health;
    }

    public string Name { get; }

    public StudioOutputState State { get; }

    public string StateText => State.ToString();

    public string Destination { get; }

    public string Bitrate { get; }

    public string Health { get; }
}

public sealed class AudioStripViewModel
{
    public AudioStripViewModel(string name, string peak, bool isMuted)
    {
        Name = name;
        Peak = peak;
        IsMuted = isMuted;
    }

    public string Name { get; }

    public string Peak { get; }

    public bool IsMuted { get; }
}

public sealed class BottomTabViewModel : ViewModelBase
{
    private bool _isSelected;

    public BottomTabViewModel(StudioBottomTabKind kind, string title, string badge = "")
    {
        Kind = kind;
        Title = title;
        Badge = badge;
    }

    public StudioBottomTabKind Kind { get; }

    public string Title { get; }

    public string Badge { get; }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public ICommand? SelectCommand { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
