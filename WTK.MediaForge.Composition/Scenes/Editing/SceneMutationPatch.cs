using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Scenes.Editing;

public abstract record SceneMutationPatch
{
    private SceneMutationPatch()
    {
    }

    public sealed record SetLayerTransform(DrawObjectId LayerId, Transform2D Transform) : SceneMutationPatch;

    public sealed record SetLayerBounds(DrawObjectId LayerId, CanvasRect Bounds) : SceneMutationPatch;

    public sealed record SetLayerVisibility(DrawObjectId LayerId, bool IsVisible) : SceneMutationPatch;

    public sealed record SetLayerOpacity(DrawObjectId LayerId, float Opacity) : SceneMutationPatch;

    public sealed record SetLayerCrop(DrawObjectId LayerId, NormalizedRect? Crop) : SceneMutationPatch;

    public sealed record SetLayerBlendMode(DrawObjectId LayerId, BlendMode BlendMode) : SceneMutationPatch;

    public sealed record SetLayerOrder(DrawObjectId LayerId, int NewIndex) : SceneMutationPatch;

    public sealed record SetLayerSource(DrawObjectId LayerId, SourceId SourceId) : SceneMutationPatch;

    public sealed record SetTextLayerContent(DrawObjectId LayerId, string Text) : SceneMutationPatch;

    public sealed record SetNestedCanvas(DrawObjectId LayerId, CanvasId NestedCanvasId) : SceneMutationPatch;

    public sealed record SetLayerEffects(DrawObjectId LayerId, IReadOnlyList<MediaForgeEffect> Effects) : SceneMutationPatch;

    public sealed record SetAdjustmentLayerMask(DrawObjectId LayerId, EffectMask? Mask) : SceneMutationPatch;

    public sealed record AddLayer(MediaForgeDrawObject Layer, int? Index = null) : SceneMutationPatch;

    public sealed record RemoveLayer(DrawObjectId LayerId) : SceneMutationPatch;
}
