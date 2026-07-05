using CommunityToolkit.Mvvm.Input;

namespace WTK.MediaForge.Studio.ViewModels.Docking;

public sealed class StudioDockPanelViewModel : ViewModelBase
{
    private bool _isVisible = true;
    private bool _isCollapsed;
    private bool _isFloating;
    private bool _isPinned = true;

    public StudioDockPanelViewModel(string id, string title, object content)
    {
        Id = id;
        Title = title;
        Content = content;
        ToggleCollapseCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
        ToggleFloatingCommand = new RelayCommand(() => IsFloating = !IsFloating);
        TogglePinnedCommand = new RelayCommand(() => IsPinned = !IsPinned);
    }

    public string Id { get; }

    public string Title { get; }

    public object Content { get; }

    public IRelayCommand ToggleCollapseCommand { get; }

    public IRelayCommand ToggleFloatingCommand { get; }

    public IRelayCommand TogglePinnedCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(IsDockedVisible));
                OnPropertyChanged(nameof(IsContentVisible));
            }
        }
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetProperty(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(IsContentVisible));
                OnPropertyChanged(nameof(CollapseGlyph));
            }
        }
    }

    public bool IsFloating
    {
        get => _isFloating;
        set
        {
            if (SetProperty(ref _isFloating, value))
            {
                OnPropertyChanged(nameof(IsDockedVisible));
                OnPropertyChanged(nameof(IsContentVisible));
                OnPropertyChanged(nameof(FloatGlyph));
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinGlyph));
            }
        }
    }

    public bool IsDockedVisible => IsVisible && !IsFloating;

    public bool IsContentVisible => IsDockedVisible && !IsCollapsed;

    public string CollapseGlyph => IsCollapsed ? "▸" : "▾";

    public string FloatGlyph => IsFloating ? "↙" : "↗";

    public string PinGlyph => IsPinned ? "●" : "○";
}
