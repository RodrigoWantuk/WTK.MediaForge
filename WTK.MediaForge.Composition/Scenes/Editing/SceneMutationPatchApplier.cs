using System.Text.Json;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Serialization;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

internal static class SceneMutationPatchApplier
{
    public static void Apply(MediaForgeProject project, CanvasId canvasId, SceneMutationPatch patch)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(patch);

        var canvas = project.Canvases.FirstOrDefault(candidate => candidate.Id == canvasId)
            ?? throw new InvalidOperationException($"Canvas {canvasId} was not found.");

        switch (patch)
        {
            case SceneMutationPatch.SetLayerTransform set:
                RequireLayer(canvas, set.LayerId).Transform = ValidateTransform(set.Transform);
                break;

            case SceneMutationPatch.SetLayerBounds set:
                RequireLayer(canvas, set.LayerId).Transform = ApplyBounds(RequireLayer(canvas, set.LayerId).Transform, set.Bounds);
                break;

            case SceneMutationPatch.SetLayerVisibility set:
                RequireLayer(canvas, set.LayerId).Enabled = set.IsVisible;
                break;

            case SceneMutationPatch.SetLayerOpacity set:
                if (!float.IsFinite(set.Opacity) || set.Opacity < 0f || set.Opacity > 1f)
                    throw new ArgumentOutOfRangeException(nameof(patch), "Layer opacity must be between 0 and 1.");
                RequireLayer(canvas, set.LayerId).Opacity = set.Opacity;
                break;

            case SceneMutationPatch.SetLayerOrder set:
                MoveLayer(canvas, set.LayerId, set.NewIndex);
                break;

            case SceneMutationPatch.SetLayerSource set:
                if (!project.SourceDefinitions.Any(source => source.Id == set.SourceId))
                    throw new InvalidOperationException($"Source {set.SourceId} was not found.");
                if (RequireLayer(canvas, set.LayerId) is not SourceLayerDrawObject sourceLayer)
                    throw new InvalidOperationException($"Layer {set.LayerId} is not a source layer.");
                sourceLayer.SourceId = set.SourceId;
                break;

            case SceneMutationPatch.SetLayerEffects set:
                var effectsLayer = RequireLayer(canvas, set.LayerId);
                effectsLayer.Effects = CloneEffects(set.Effects);
                break;

            case SceneMutationPatch.AddLayer add:
                ArgumentNullException.ThrowIfNull(add.Layer);
                var layer = CloneLayer(add.Layer);
                if (layer.Id.IsEmpty)
                    layer.Id = DrawObjectId.New();
                InsertLayer(canvas, layer, add.Index);
                break;

            case SceneMutationPatch.RemoveLayer remove:
                var existing = RequireLayer(canvas, remove.LayerId);
                canvas.Objects.Remove(existing);
                break;

            default:
                throw new NotSupportedException($"Unsupported scene mutation patch '{patch.GetType().Name}'.");
        }
    }

    private static MediaForgeDrawObject RequireLayer(MediaForgeCanvas canvas, DrawObjectId layerId) =>
        canvas.Objects.FirstOrDefault(layer => layer.Id == layerId)
        ?? throw new InvalidOperationException($"Layer {layerId} was not found in canvas '{canvas.Name}'.");

    private static Transform2D ValidateTransform(Transform2D transform)
    {
        if (!transform.HasPositiveSize)
            throw new ArgumentException("Layer transform must have a positive size.", nameof(transform));

        if (!float.IsFinite(transform.Position.X) ||
            !float.IsFinite(transform.Position.Y) ||
            !float.IsFinite(transform.RotationDegrees))
        {
            throw new ArgumentException("Layer transform contains non-finite values.", nameof(transform));
        }

        return transform;
    }

    private static Transform2D ApplyBounds(Transform2D current, CanvasRect bounds)
    {
        if (!float.IsFinite(bounds.X) ||
            !float.IsFinite(bounds.Y) ||
            !float.IsFinite(bounds.Width) ||
            !float.IsFinite(bounds.Height) ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            throw new ArgumentException("Layer bounds must be finite and positive.", nameof(bounds));
        }

        return current with
        {
            Position = new CanvasPoint(bounds.X, bounds.Y),
            Size = new CanvasSize(bounds.Width, bounds.Height)
        };
    }

    private static void MoveLayer(MediaForgeCanvas canvas, DrawObjectId layerId, int newIndex)
    {
        if (newIndex < 0 || newIndex >= canvas.Objects.Count)
            throw new ArgumentOutOfRangeException(nameof(newIndex), "Layer order index is outside the canvas layer range.");

        var layer = RequireLayer(canvas, layerId);
        var oldIndex = canvas.Objects.IndexOf(layer);
        if (oldIndex == newIndex)
            return;

        canvas.Objects.RemoveAt(oldIndex);
        canvas.Objects.Insert(newIndex, layer);
    }

    private static void InsertLayer(MediaForgeCanvas canvas, MediaForgeDrawObject layer, int? index)
    {
        if (index is null)
        {
            canvas.Objects.Add(layer);
            return;
        }

        if (index.Value < 0 || index.Value > canvas.Objects.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Layer insert index is outside the canvas layer range.");

        canvas.Objects.Insert(index.Value, layer);
    }

    private static MediaForgeDrawObject CloneLayer(MediaForgeDrawObject layer) =>
        JsonSerializer.Deserialize<MediaForgeDrawObject>(
            JsonSerializer.Serialize(layer, MediaForgeProjectJsonOptions.Create()),
            MediaForgeProjectJsonOptions.Create())
        ?? throw new InvalidOperationException("Failed to clone scene layer mutation payload.");

    private static List<MediaForgeEffect> CloneEffects(IReadOnlyList<MediaForgeEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        return JsonSerializer.Deserialize<List<MediaForgeEffect>>(
            JsonSerializer.Serialize(effects, MediaForgeProjectJsonOptions.Create()),
            MediaForgeProjectJsonOptions.Create())
        ?? throw new InvalidOperationException("Failed to clone scene effect mutation payload.");
    }
}
