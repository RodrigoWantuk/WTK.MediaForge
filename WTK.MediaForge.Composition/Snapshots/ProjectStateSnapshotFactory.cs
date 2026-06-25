using System.Collections.Immutable;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class ProjectStateSnapshotFactory
{
    private static long _snapshotCounter;

    public static ProjectStateSnapshot CreateImmutableSnapshot(MediaForgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new ProjectStateSnapshot
        {
            Version = Interlocked.Increment(ref _snapshotCounter),
            Sources = project.SourceDefinitions.Select(CloneSource).ToImmutableArray(),
            Canvases = project.Canvases.Select(CloneCanvas).ToImmutableArray(),
            Outputs = project.Outputs.Select(CloneOutput).ToImmutableArray()
        };
    }

    private static SourceDefinitionSnapshot CloneSource(MediaForgeSourceDefinition source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            TypeId = source.TypeId,
            SchemaVersion = source.SchemaVersion,
            Settings = JsonObjectCloneHelper.DeepClone(source.Settings)
        };

    private static CanvasStateSnapshot CloneCanvas(MediaForgeCanvas canvas) =>
        new()
        {
            Id = canvas.Id,
            Name = canvas.Name,
            Size = canvas.Size,
            BackgroundColor = canvas.BackgroundColor,
            Objects = canvas.Objects.Select(CloneDrawObject).ToImmutableArray()
        };

    private static RenderOutputStateSnapshot CloneOutput(MediaForgeRenderOutput output) =>
        new()
        {
            Id = output.Id,
            Name = output.Name,
            TypeId = output.TypeId,
            SchemaVersion = output.SchemaVersion,
            Settings = JsonObjectCloneHelper.DeepClone(output.Settings),
            CanvasId = output.CanvasId,
            OutputSize = output.OutputSize,
            CanvasLayoutMode = output.CanvasLayoutMode,
            LetterboxColor = output.LetterboxColor
        };

    private static DrawObjectStateSnapshot CloneDrawObject(MediaForgeDrawObject drawObject)
    {
        var effects = EffectSnapshotFactory.CloneEffects(drawObject.Effects);

        return drawObject switch
        {
            SourceLayerDrawObject sourceLayer => new SourceLayerDrawObjectSnapshot
            {
                Id = sourceLayer.Id,
                Name = sourceLayer.Name,
                Enabled = sourceLayer.Enabled,
                Transform = sourceLayer.Transform,
                Crop = sourceLayer.Crop,
                Opacity = sourceLayer.Opacity,
                BlendMode = sourceLayer.BlendMode,
                Effects = effects,
                SourceId = sourceLayer.SourceId,
                LayoutMode = sourceLayer.LayoutMode,
                LetterboxColor = sourceLayer.LetterboxColor,
                ContentRotationOverride = sourceLayer.ContentRotationOverride
            },
            TextDrawObject text => new TextDrawObjectSnapshot
            {
                Id = text.Id,
                Name = text.Name,
                Enabled = text.Enabled,
                Transform = text.Transform,
                Crop = text.Crop,
                Opacity = text.Opacity,
                BlendMode = text.BlendMode,
                Effects = effects,
                Text = text.Text,
                TextColor = text.TextColor,
                FontSize = text.FontSize
            },
            SolidDrawObject solid => new SolidDrawObjectSnapshot
            {
                Id = solid.Id,
                Name = solid.Name,
                Enabled = solid.Enabled,
                Transform = solid.Transform,
                Crop = solid.Crop,
                Opacity = solid.Opacity,
                BlendMode = solid.BlendMode,
                Effects = effects,
                FillColor = solid.FillColor
            },
            CanvasDrawObject nested => new CanvasDrawObjectSnapshot
            {
                Id = nested.Id,
                Name = nested.Name,
                Enabled = nested.Enabled,
                Transform = nested.Transform,
                Crop = nested.Crop,
                Opacity = nested.Opacity,
                BlendMode = nested.BlendMode,
                Effects = effects,
                NestedCanvasId = nested.NestedCanvasId
            },
            _ => throw new NotSupportedException($"Unsupported draw object type: {drawObject.GetType().Name}.")
        };
    }
}
