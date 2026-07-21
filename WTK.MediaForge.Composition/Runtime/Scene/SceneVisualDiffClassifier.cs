using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal static class SceneVisualDiffClassifier
{
    public static SceneDirtyKind Classify(
        DrawObjectStateSnapshot previous,
        DrawObjectStateSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.Id != current.Id || previous.GetType() != current.GetType())
            return SceneDirtyKind.Structure;

        if (DrawObjectVisualStateFingerprint.Create(previous) ==
            DrawObjectVisualStateFingerprint.Create(current))
        {
            return SceneDirtyKind.None;
        }

        if (!EffectStateFingerprint.SequenceEquals(previous.Effects, current.Effects))
            return SceneDirtyKind.Effects;

        if (TransformEqual(previous, current))
            return SceneDirtyKind.Transform;

        return IsStructuralBindingChange(previous, current)
            ? SceneDirtyKind.Structure
            : SceneDirtyKind.Full;
    }

    private static bool TransformEqual(DrawObjectStateSnapshot previous, DrawObjectStateSnapshot current) =>
        previous.Enabled == current.Enabled &&
        previous.BlendMode == current.BlendMode &&
        TypeSpecificVisualStateEqual(previous, current) &&
        !TransformsAndPlacementEqual(previous, current);

    private static bool TransformsAndPlacementEqual(DrawObjectStateSnapshot left, DrawObjectStateSnapshot right) =>
        left.Transform.Equals(right.Transform) &&
        left.Opacity == right.Opacity &&
        left.Crop == right.Crop;

    private static bool TypeSpecificVisualStateEqual(
        DrawObjectStateSnapshot previous,
        DrawObjectStateSnapshot current) =>
        (previous, current) switch
        {
            (SourceLayerDrawObjectSnapshot left, SourceLayerDrawObjectSnapshot right) =>
                left.SourceId == right.SourceId &&
                left.LayoutMode == right.LayoutMode &&
                left.LetterboxColor.Equals(right.LetterboxColor) &&
                left.ContentRotationOverride == right.ContentRotationOverride,
            (TextDrawObjectSnapshot left, TextDrawObjectSnapshot right) =>
                left.Text == right.Text &&
                left.FontFamily == right.FontFamily &&
                left.FontSize == right.FontSize &&
                left.TextColor.Equals(right.TextColor),
            (SolidDrawObjectSnapshot left, SolidDrawObjectSnapshot right) =>
                left.FillColor.Equals(right.FillColor),
            (CanvasDrawObjectSnapshot left, CanvasDrawObjectSnapshot right) =>
                left.NestedCanvasId == right.NestedCanvasId &&
                left.VersionBinding == right.VersionBinding,
            _ => throw new NotSupportedException(
                $"Draw object type '{previous.GetType().FullName}' must define explicit visual diff semantics.")
        };

    private static bool IsStructuralBindingChange(
        DrawObjectStateSnapshot previous,
        DrawObjectStateSnapshot current) =>
        (previous, current) switch
        {
            (SourceLayerDrawObjectSnapshot left, SourceLayerDrawObjectSnapshot right) => left.SourceId != right.SourceId,
            (CanvasDrawObjectSnapshot left, CanvasDrawObjectSnapshot right) => left.NestedCanvasId != right.NestedCanvasId,
            (TextDrawObjectSnapshot, TextDrawObjectSnapshot) => false,
            (SolidDrawObjectSnapshot, SolidDrawObjectSnapshot) => false,
            _ => true
        };
}
