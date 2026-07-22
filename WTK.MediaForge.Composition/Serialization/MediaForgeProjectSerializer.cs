using System.Text.Json;
using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.Serialization;

namespace WTK.MediaForge.Composition.Project;

public static class MediaForgeProjectSerializer
{
    public static string Serialize(MediaForgeProject project) =>
        JsonSerializer.Serialize(project, MediaForgeProjectJsonOptions.Create());

    public static MediaForgeProject Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("Project JSON deserialized to null.");
        LegacyTransitionEffectMigration.Apply(root);
        return root.Deserialize<MediaForgeProject>(MediaForgeProjectJsonOptions.Create())
            ?? throw new JsonException("Project JSON deserialized to null.");
    }
}

internal static class LegacyTransitionEffectMigration
{
    private const string LegacyDiscriminator = "effect.transition";

    public static void Apply(JsonObject project)
    {
        var schemaVersion = project["schemaVersion"]?.GetValue<int>() ?? 1;
        var legacyCount = CountLegacyTransitions(project);
        if (legacyCount == 0)
            return;

        if (schemaVersion >= MediaForgeProject.CurrentSchemaVersion)
        {
            throw new JsonException(
                $"'{LegacyDiscriminator}' is not an effect type. Use the scene/output transition model.");
        }

        RemoveLegacyTransitions(project);
        project["schemaVersion"] = MediaForgeProject.CurrentSchemaVersion;
    }

    private static int CountLegacyTransitions(JsonNode? node)
    {
        if (node is JsonObject obj)
            return (obj["$type"]?.GetValue<string>() == LegacyDiscriminator ? 1 : 0) +
                obj.Sum(static pair => CountLegacyTransitions(pair.Value));
        if (node is JsonArray array)
            return array.Sum(CountLegacyTransitions);
        return 0;
    }

    private static void RemoveLegacyTransitions(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
                RemoveLegacyTransitions(pair.Value);
            return;
        }

        if (node is not JsonArray array)
            return;

        for (var index = array.Count - 1; index >= 0; index--)
        {
            if (array[index] is JsonObject item &&
                item["$type"]?.GetValue<string>() == LegacyDiscriminator)
            {
                array.RemoveAt(index);
            }
            else
            {
                RemoveLegacyTransitions(array[index]);
            }
        }
    }
}
