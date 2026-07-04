using System.Text.Json;
using WTK.MediaForge.Composition.Serialization;

namespace WTK.MediaForge.Composition.Project.Packages;

public static class MediaForgePackageSerializer
{
    public static string SerializeScene(MediaForgeScenePackage package) =>
        Serialize(package);

    public static MediaForgeScenePackage DeserializeScene(string json) =>
        Deserialize<MediaForgeScenePackage>(json);

    public static string SerializeCanvasPreset(MediaForgeCanvasPreset preset) =>
        Serialize(preset);

    public static MediaForgeCanvasPreset DeserializeCanvasPreset(string json) =>
        Deserialize<MediaForgeCanvasPreset>(json);

    public static string SerializeSourcePreset(MediaForgeSourcePreset preset) =>
        Serialize(preset);

    public static MediaForgeSourcePreset DeserializeSourcePreset(string json) =>
        Deserialize<MediaForgeSourcePreset>(json);

    public static string SerializeOutputPreset(MediaForgeOutputPreset preset) =>
        Serialize(preset);

    public static MediaForgeOutputPreset DeserializeOutputPreset(string json) =>
        Deserialize<MediaForgeOutputPreset>(json);

    public static string SerializeEffectPreset(MediaForgeEffectPreset preset) =>
        Serialize(preset);

    public static MediaForgeEffectPreset DeserializeEffectPreset(string json) =>
        Deserialize<MediaForgeEffectPreset>(json);

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, MediaForgeProjectJsonOptions.Create());

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, MediaForgeProjectJsonOptions.Create())
        ?? throw new JsonException($"{typeof(T).Name} JSON deserialized to null.");
}
