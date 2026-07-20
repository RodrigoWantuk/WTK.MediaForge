using CommunityToolkit.Mvvm.ComponentModel;
using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.Services;

public sealed class SceneEditSession : ObservableObject
{
    private bool _hasChanges;

    public SceneEditSession(StudioScene original, StudioScene draft)
    {
        Original = original;
        Draft = draft;
    }

    public string SceneId => Original.Id;

    public StudioScene Original { get; }

    public StudioScene Draft { get; private set; }

    public bool HasChanges
    {
        get => _hasChanges;
        private set => SetProperty(ref _hasChanges, value);
    }

    public void MarkChanged()
    {
        HasChanges = true;
    }

    public void MarkClean()
    {
        HasChanges = false;
    }

    public void SetHasChanges(bool hasChanges)
    {
        HasChanges = hasChanges;
    }

    public void RestoreDraft(StudioScene draft, bool hasChanges)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Draft = draft;
        HasChanges = hasChanges;
        OnPropertyChanged(nameof(Draft));
    }
}

public sealed class SceneEditSessionService
{
    public SceneEditSession Create(StudioScene scene)
    {
        return new SceneEditSession(scene, CloneScene(scene));
    }

    public void Apply(SceneEditSession session)
    {
        CopyScene(session.Draft, session.Original);
        session.MarkClean();
    }

    public static StudioScene CloneScene(StudioScene source)
    {
        var clone = new StudioScene
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Metadata = source.Metadata,
            IsProgram = source.IsProgram
        };
        CopyCanvas(source.Canvas, clone.Canvas);
        foreach (var layer in source.Layers)
        {
            clone.Layers.Add(CloneLayer(layer));
        }

        foreach (var effect in source.Effects)
        {
            clone.Effects.Add(CloneEffect(effect));
        }

        foreach (var outputId in source.OutputIds)
        {
            clone.OutputIds.Add(outputId);
        }

        return clone;
    }

    private static void CopyScene(StudioScene source, StudioScene target)
    {
        target.DisplayName = source.DisplayName;
        target.Metadata = source.Metadata;
        target.IsProgram = source.IsProgram;
        CopyCanvas(source.Canvas, target.Canvas);

        target.Layers.Clear();
        foreach (var layer in source.Layers)
        {
            target.Layers.Add(CloneLayer(layer));
        }

        target.Effects.Clear();
        foreach (var effect in source.Effects)
        {
            target.Effects.Add(CloneEffect(effect));
        }

        target.OutputIds.Clear();
        foreach (var outputId in source.OutputIds)
        {
            target.OutputIds.Add(outputId);
        }
    }

    private static StudioLayer CloneLayer(StudioLayer source)
    {
        var clone = new StudioLayer
        {
            Id = source.Id,
            Name = source.Name,
            SourceId = source.SourceId,
            SourceName = source.SourceName,
            Type = source.Type,
            Order = source.Order,
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            BlendMode = source.BlendMode,
            Crop = source.Crop
        };
        CopyTransform(source.Transform, clone.Transform);
        foreach (var effect in source.Effects)
        {
            clone.Effects.Add(CloneEffect(effect));
        }

        return clone;
    }

    private static StudioEffect CloneEffect(StudioEffect source)
    {
        return new StudioEffect
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            IsEnabled = source.IsEnabled,
            IsExpanded = source.IsExpanded,
            KeyColor = source.KeyColor,
            Tolerance = source.Tolerance,
            Spill = source.Spill,
            EdgeSmooth = source.EdgeSmooth
        };
    }

    private static void CopyCanvas(StudioCanvasSettings source, StudioCanvasSettings target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.FrameRate = source.FrameRate;
        target.BackgroundColor = source.BackgroundColor;
    }

    private static void CopyTransform(StudioTransform source, StudioTransform target)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.RotationDegrees = source.RotationDegrees;
        target.Opacity = source.Opacity;
    }
}
