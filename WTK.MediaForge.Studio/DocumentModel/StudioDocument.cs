using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.DocumentModel;

public sealed partial class StudioDocument : ObservableObject
{
    private string _id = "studio-document";
    private string _displayName = "Live Production Workspace";
    private bool _hasUnsavedChanges;
    private string _selectedSceneId = "scene-main";

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public string SelectedSceneId
    {
        get => _selectedSceneId;
        set => SetProperty(ref _selectedSceneId, value);
    }

    public ObservableCollection<StudioScene> Scenes { get; } = new();

    public ObservableCollection<StudioSource> Sources { get; } = new();

    public ObservableCollection<StudioOutput> Outputs { get; } = new();

    public ObservableCollection<StudioPreset> Presets { get; } = new();

    public ObservableCollection<StudioPackage> Packages { get; } = new();
}

public sealed partial class StudioScene : ObservableObject
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _metadata = string.Empty;
    private bool _isProgram;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
    }

    public bool IsProgram
    {
        get => _isProgram;
        set => SetProperty(ref _isProgram, value);
    }

    public StudioCanvasSettings Canvas { get; } = new();

    public ObservableCollection<StudioLayer> Layers { get; } = new();

    public ObservableCollection<string> OutputIds { get; } = new();
}

public sealed partial class StudioSource : ObservableObject
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _typeId = string.Empty;
    private string _metadata = string.Empty;
    private string _endpoint = string.Empty;
    private StudioHealthState _health = StudioHealthState.Healthy;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string TypeId
    {
        get => _typeId;
        set => SetProperty(ref _typeId, value);
    }

    public string Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    public StudioHealthState Health
    {
        get => _health;
        set => SetProperty(ref _health, value);
    }
}

public sealed partial class StudioLayer : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _sourceId = string.Empty;
    private string _sourceName = string.Empty;
    private string _type = "Source";
    private int _order;
    private bool _isVisible = true;
    private bool _isLocked;
    private StudioBlendMode _blendMode = StudioBlendMode.Alpha;
    private StudioCropThickness _crop;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SourceId
    {
        get => _sourceId;
        set => SetProperty(ref _sourceId, value);
    }

    public string SourceName
    {
        get => _sourceName;
        set => SetProperty(ref _sourceName, value);
    }

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
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

    public StudioBlendMode BlendMode
    {
        get => _blendMode;
        set => SetProperty(ref _blendMode, value);
    }

    public StudioCropThickness Crop
    {
        get => _crop;
        set => SetProperty(ref _crop, value);
    }

    public StudioTransform Transform { get; } = new();

    public ObservableCollection<StudioEffect> Effects { get; } = new();
}

public sealed partial class StudioEffect : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled;
    private bool _isExpanded;
    private string _keyColor = "#24FF71";
    private double _tolerance = 0.32;
    private double _spill = 0.18;
    private double _edgeSmooth = 0.24;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

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

    public string KeyColor
    {
        get => _keyColor;
        set => SetProperty(ref _keyColor, value);
    }

    public double Tolerance
    {
        get => _tolerance;
        set => SetProperty(ref _tolerance, Math.Clamp(value, 0, 1));
    }

    public double Spill
    {
        get => _spill;
        set => SetProperty(ref _spill, Math.Clamp(value, 0, 1));
    }

    public double EdgeSmooth
    {
        get => _edgeSmooth;
        set => SetProperty(ref _edgeSmooth, Math.Clamp(value, 0, 1));
    }
}

public sealed partial class StudioOutput : ObservableObject
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _typeId = string.Empty;
    private string _destination = string.Empty;
    private string _codec = string.Empty;
    private string _bitrate = string.Empty;
    private string _secret = string.Empty;
    private StudioOutputState _state = StudioOutputState.Running;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string TypeId
    {
        get => _typeId;
        set => SetProperty(ref _typeId, value);
    }

    public string Destination
    {
        get => _destination;
        set => SetProperty(ref _destination, value);
    }

    public string Codec
    {
        get => _codec;
        set => SetProperty(ref _codec, value);
    }

    public string Bitrate
    {
        get => _bitrate;
        set => SetProperty(ref _bitrate, value);
    }

    public string Secret
    {
        get => _secret;
        set => SetProperty(ref _secret, value);
    }

    public StudioOutputState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
}

public sealed class StudioPreset
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Metadata { get; init; } = string.Empty;

    public string TypeId { get; init; } = string.Empty;
}

public sealed class StudioPackage
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Metadata { get; init; } = string.Empty;

    public string TypeId { get; init; } = string.Empty;
}

public sealed partial class StudioTransform : ObservableObject
{
    private double _x;
    private double _y;
    private double _width = 320;
    private double _height = 180;
    private double _rotationDegrees;
    private double _opacity = 100;

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, Math.Max(16, value));
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, Math.Max(16, value));
    }

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set => SetProperty(ref _rotationDegrees, Math.Clamp(value, -360, 360));
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 0, 100));
    }
}

public sealed partial class StudioCanvasSettings : ObservableObject
{
    private double _width = 1920;
    private double _height = 1080;
    private double _frameRate = 60;
    private string _backgroundColor = "#10141B";

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public double FrameRate
    {
        get => _frameRate;
        set => SetProperty(ref _frameRate, value);
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set => SetProperty(ref _backgroundColor, value);
    }
}
