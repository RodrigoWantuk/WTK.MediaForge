using WTK.MediaForge.Composition.Serialization;

namespace WTK.MediaForge.Composition.Project;

internal static class MediaForgeProjectCloner
{
    public static MediaForgeProject DeepClone(MediaForgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var json = MediaForgeProjectSerializer.Serialize(project);
        return MediaForgeProjectSerializer.Deserialize(json);
    }
}
