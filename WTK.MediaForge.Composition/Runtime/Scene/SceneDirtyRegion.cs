using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class SceneDirtyRegion
{
    public static SceneDirtyRegion Full { get; } = new() { GlobalKind = SceneDirtyKind.Full };

    public SceneDirtyKind GlobalKind { get; init; } = SceneDirtyKind.None;

    public IReadOnlyDictionary<DrawObjectId, SceneDirtyKind> LayerDirtyKinds { get; init; } =
        new Dictionary<DrawObjectId, SceneDirtyKind>();

    public bool RequiresGraphRecompile =>
        GlobalKind is SceneDirtyKind.Structure or SceneDirtyKind.Full ||
        LayerDirtyKinds.Values.Any(kind => kind is SceneDirtyKind.Structure or SceneDirtyKind.Full or SceneDirtyKind.Effects);
}
