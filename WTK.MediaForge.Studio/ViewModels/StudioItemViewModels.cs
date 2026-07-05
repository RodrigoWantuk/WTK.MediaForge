using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Localization;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.ViewModels;

public sealed class ProjectTreeGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private string _searchText = string.Empty;

    public ProjectTreeGroupViewModel(string title, IEnumerable<ProjectTreeItemViewModel> items)
    {
        Title = title;
        Items = new ObservableCollection<ProjectTreeItemViewModel>(items);
        VisibleItems = new ObservableCollection<ProjectTreeItemViewModel>(Items);
    }

    public string Title { get; }

    public ObservableCollection<ProjectTreeItemViewModel> Items { get; }

    public ObservableCollection<ProjectTreeItemViewModel> VisibleItems { get; }

    public int Count => VisibleItems.Count;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void ApplyFilter(string searchText)
    {
        _searchText = searchText ?? string.Empty;
        VisibleItems.Clear();

        foreach (var item in Items.Where(MatchesFilter))
        {
            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(Count));
    }

    private bool MatchesFilter(ProjectTreeItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        return item.SearchText.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
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
        StudioIconKind iconKind,
        string badge = "",
        StudioHealthState healthState = StudioHealthState.Healthy,
        string id = "",
        string typeId = "",
        string detail = "",
        string destination = "",
        string codec = "",
        string bitrate = "",
        string secret = "")
    {
        Kind = kind;
        Id = string.IsNullOrWhiteSpace(id) ? $"{kind}:{name}".ToLowerInvariant().Replace(' ', '-') : id;
        Name = name;
        Metadata = metadata;
        IconKind = iconKind;
        Badge = badge;
        HealthState = healthState;
        TypeId = string.IsNullOrWhiteSpace(typeId) ? kind.ToString().ToLowerInvariant() : typeId;
        Detail = detail;
        Destination = destination;
        Codec = codec;
        Bitrate = bitrate;
        Secret = secret;
    }

    public StudioProjectItemKind Kind { get; }

    public string Id { get; }

    public string Name { get; }

    public string Metadata { get; }

    public StudioIconKind IconKind { get; }

    public string Badge { get; }

    public StudioHealthState HealthState { get; }

    public string TypeId { get; }

    public string Detail { get; }

    public string Destination { get; }

    public string Codec { get; }

    public string Bitrate { get; }

    public string Secret { get; }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public string SearchText => $"{Name} {Metadata} {Badge} {TypeId} {Destination} {Codec} {Bitrate}";

    public bool IsHealthy => HealthState == StudioHealthState.Healthy;

    public bool IsWarning => HealthState == StudioHealthState.Warning;

    public bool IsError => HealthState == StudioHealthState.Error;

    public bool IsPlanned => HealthState == StudioHealthState.Planned;

    public bool IsDisabled => HealthState == StudioHealthState.Disabled;

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
    private readonly StudioLayer _layer;
    private bool _isSelected;

    public LayerItemViewModel(string name, string source, string type, StudioIconKind iconKind, int order, string id = "")
        : this(new StudioLayer
        {
            Id = string.IsNullOrWhiteSpace(id) ? $"layer:{name}".ToLowerInvariant().Replace(' ', '-') : id,
            Name = name,
            SourceName = source,
            SourceId = $"source:{source}".ToLowerInvariant().Replace(' ', '-'),
            Type = type,
            Order = order
        }, iconKind)
    {
    }

    public LayerItemViewModel(StudioLayer layer, StudioIconKind iconKind)
    {
        _layer = layer;
        IconKind = iconKind;

        _layer.PropertyChanged += OnLayerPropertyChanged;
        _layer.Transform.PropertyChanged += OnTransformPropertyChanged;

        Effects = new ObservableCollection<EffectItemViewModel>(_layer.Effects.Select(effect => new EffectItemViewModel(effect)));
    }

    public StudioLayer Layer => _layer;

    public string Name => _layer.Name;

    public string Id => _layer.Id;

    public string Source => _layer.SourceName;

    public string Type => _layer.Type;

    public StudioIconKind IconKind { get; }

    public int Order
    {
        get => _layer.Order;
        set => _layer.Order = value;
    }

    public ObservableCollection<EffectItemViewModel> Effects { get; }

    public ICommand? SelectCommand { get; set; }

    public ICommand? ToggleVisibilityCommand { get; set; }

    public ICommand? ToggleLockCommand { get; set; }

    public ICommand? MoveUpCommand { get; set; }

    public ICommand? MoveDownCommand { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _layer.IsVisible;
        set
        {
            if (_layer.IsVisible != value)
            {
                _layer.IsVisible = value;
            }
        }
    }

    public bool IsLocked
    {
        get => _layer.IsLocked;
        set
        {
            if (_layer.IsLocked != value)
            {
                _layer.IsLocked = value;
            }
        }
    }

    public double X
    {
        get => _layer.Transform.X;
        set => _layer.Transform.X = value;
    }

    public double Y
    {
        get => _layer.Transform.Y;
        set => _layer.Transform.Y = value;
    }

    public double Width
    {
        get => _layer.Transform.Width;
        set => _layer.Transform.Width = value;
    }

    public double Height
    {
        get => _layer.Transform.Height;
        set => _layer.Transform.Height = value;
    }

    public double RotationDegrees
    {
        get => _layer.Transform.RotationDegrees;
        set => _layer.Transform.RotationDegrees = value;
    }

    public double Opacity
    {
        get => _layer.Transform.Opacity;
        set => _layer.Transform.Opacity = value;
    }

    public double LayerOpacity => Opacity / 100d;

    public StudioCropThickness Crop
    {
        get => _layer.Crop;
        set => _layer.Crop = value;
    }

    public double CropLeft
    {
        get => Crop.Left;
        set => Crop = Crop with { Left = Math.Max(0, value) };
    }

    public double CropTop
    {
        get => Crop.Top;
        set => Crop = Crop with { Top = Math.Max(0, value) };
    }

    public double CropRight
    {
        get => Crop.Right;
        set => Crop = Crop with { Right = Math.Max(0, value) };
    }

    public double CropBottom
    {
        get => Crop.Bottom;
        set => Crop = Crop with { Bottom = Math.Max(0, value) };
    }

    public StudioBlendMode BlendMode
    {
        get => _layer.BlendMode;
        set => _layer.BlendMode = value;
    }

    public IEnumerable<StudioBlendMode> AvailableBlendModes { get; } = Enum.GetValues<StudioBlendMode>();

    public string BlendModeDisplayName => new StudioDisplayNameService().GetBlendModeName(BlendMode);

    public string VisibilityGlyph => IsVisible ? "Visible" : "Hidden";

    public string VisibilityTip => IsVisible ? "Layer visible" : "Layer hidden";

    public StudioIconKind VisibilityIconKind => IsVisible ? StudioIconKind.Eye : StudioIconKind.EyeOff;

    public string LockGlyph => IsLocked ? "Locked" : "Editable";

    public string LockTip => IsLocked ? "Layer locked" : "Layer editable";

    public StudioIconKind LockIconKind => IsLocked ? StudioIconKind.Lock : StudioIconKind.Unlock;

    public void MoveBy(double deltaX, double deltaY, double canvasWidth, double canvasHeight)
    {
        if (IsLocked)
        {
            return;
        }

        X = Math.Clamp(X + deltaX, 0, Math.Max(0, canvasWidth - Width));
        Y = Math.Clamp(Y + deltaY, 0, Math.Max(0, canvasHeight - Height));
    }

    public void Resize(ResizeHandleKind handle, double deltaX, double deltaY, double canvasWidth, double canvasHeight, bool keepAspect, bool fromCenter)
    {
        if (IsLocked || handle == ResizeHandleKind.None)
        {
            return;
        }

        const double minSize = 16;
        var x = X;
        var y = Y;
        var width = Width;
        var height = Height;
        var aspect = height > 0 ? width / height : 1;

        var left = handle is ResizeHandleKind.Left or ResizeHandleKind.TopLeft or ResizeHandleKind.BottomLeft;
        var right = handle is ResizeHandleKind.Right or ResizeHandleKind.TopRight or ResizeHandleKind.BottomRight;
        var top = handle is ResizeHandleKind.Top or ResizeHandleKind.TopLeft or ResizeHandleKind.TopRight;
        var bottom = handle is ResizeHandleKind.Bottom or ResizeHandleKind.BottomLeft or ResizeHandleKind.BottomRight;

        if (fromCenter)
        {
            if (left || right)
            {
                x -= deltaX;
                width += deltaX * 2;
            }

            if (top || bottom)
            {
                y -= deltaY;
                height += deltaY * 2;
            }
        }
        else
        {
            if (left)
            {
                x += deltaX;
                width -= deltaX;
            }
            else if (right)
            {
                width += deltaX;
            }

            if (top)
            {
                y += deltaY;
                height -= deltaY;
            }
            else if (bottom)
            {
                height += deltaY;
            }
        }

        width = Math.Max(minSize, width);
        height = Math.Max(minSize, height);

        if (keepAspect)
        {
            if (Math.Abs(deltaX) >= Math.Abs(deltaY))
            {
                height = width / aspect;
            }
            else
            {
                width = height * aspect;
            }
        }

        x = Math.Clamp(x, 0, Math.Max(0, canvasWidth - minSize));
        y = Math.Clamp(y, 0, Math.Max(0, canvasHeight - minSize));
        width = Math.Min(width, canvasWidth - x);
        height = Math.Min(height, canvasHeight - y);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName is nameof(StudioLayer.IsVisible))
        {
            OnPropertyChanged(nameof(VisibilityGlyph));
            OnPropertyChanged(nameof(VisibilityTip));
            OnPropertyChanged(nameof(VisibilityIconKind));
        }
        else if (e.PropertyName is nameof(StudioLayer.IsLocked))
        {
            OnPropertyChanged(nameof(LockGlyph));
            OnPropertyChanged(nameof(LockTip));
            OnPropertyChanged(nameof(LockIconKind));
        }
        else if (e.PropertyName is nameof(StudioLayer.BlendMode))
        {
            OnPropertyChanged(nameof(BlendModeDisplayName));
        }
        else if (e.PropertyName is nameof(StudioLayer.Crop))
        {
            OnPropertyChanged(nameof(CropLeft));
            OnPropertyChanged(nameof(CropTop));
            OnPropertyChanged(nameof(CropRight));
            OnPropertyChanged(nameof(CropBottom));
        }
    }

    private void OnTransformPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName is nameof(StudioTransform.Opacity))
        {
            OnPropertyChanged(nameof(LayerOpacity));
        }
    }
}

public sealed class EffectItemViewModel : ViewModelBase
{
    private readonly StudioEffect _effect;

    public EffectItemViewModel(string name, string description, bool isEnabled, bool isExpanded)
        : this(new StudioEffect
        {
            Id = $"effect:{name}".ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            IsExpanded = isExpanded
        })
    {
    }

    public EffectItemViewModel(StudioEffect effect)
    {
        _effect = effect;
        _effect.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(StudioEffect.IsEnabled))
            {
                OnPropertyChanged(nameof(EnabledText));
            }
        };
    }

    public StudioEffect Effect => _effect;

    public string Name => _effect.Name;

    public string Description => _effect.Description;

    public ICommand? ToggleEnabledCommand { get; set; }

    public bool IsEnabled
    {
        get => _effect.IsEnabled;
        set
        {
            if (_effect.IsEnabled != value)
            {
                _effect.IsEnabled = value;
            }
        }
    }

    public string EnabledText => IsEnabled ? "Enabled" : "Disabled";

    public bool IsExpanded
    {
        get => _effect.IsExpanded;
        set => _effect.IsExpanded = value;
    }

    public string KeyColor
    {
        get => _effect.KeyColor;
        set => _effect.KeyColor = value;
    }

    public double Tolerance
    {
        get => _effect.Tolerance;
        set => _effect.Tolerance = value;
    }

    public double Spill
    {
        get => _effect.Spill;
        set => _effect.Spill = value;
    }

    public double EdgeSmooth
    {
        get => _effect.EdgeSmooth;
        set => _effect.EdgeSmooth = value;
    }
}

public sealed class DiagnosticLogItemViewModel
{
    public DiagnosticLogItemViewModel(string time, string level, string message, string category = "Studio")
    {
        Time = time;
        Level = level;
        Message = message;
        Category = category;
    }

    public string Time { get; }

    public string Level { get; }

    public string Category { get; }

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
    public OutputMonitorItemViewModel(string name, StudioOutputState state, string destination, string bitrate, string health, string type = "")
    {
        Name = name;
        State = state;
        Destination = destination;
        Bitrate = bitrate;
        Health = health;
        Type = type;
    }

    public string Name { get; }

    public string Type { get; }

    public StudioOutputState State { get; }

    public string StateText => new StudioDisplayNameService().GetOutputMonitorStateName(State);

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

    public string MuteText => IsMuted ? "Muted" : "Active";
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
