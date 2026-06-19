using System.Text.Json;
using WTK.MediaForge.Composition.Serialization;

namespace WTK.MediaForge.Composition.Project;

public static class MediaForgeProjectSerializer
{
    public static string Serialize(MediaForgeProject project) =>
        JsonSerializer.Serialize(project, MediaForgeProjectJsonOptions.Create());

    public static MediaForgeProject Deserialize(string json) =>
        JsonSerializer.Deserialize<MediaForgeProject>(json, MediaForgeProjectJsonOptions.Create())
        ?? throw new JsonException("Project JSON deserialized to null.");
}
