using System.Text.Json.Nodes;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class JsonObjectCloneHelper
{
    public static JsonObject DeepClone(JsonObject? source) =>
        source is null
            ? new JsonObject()
            : JsonNode.Parse(source.ToJsonString())!.AsObject();
}
