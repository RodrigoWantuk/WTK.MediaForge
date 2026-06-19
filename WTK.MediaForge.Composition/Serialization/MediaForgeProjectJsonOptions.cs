using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.DrawObjects;

namespace WTK.MediaForge.Composition.Serialization;

public static class MediaForgeProjectJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new SourceIdJsonConverter());
        options.Converters.Add(new CanvasIdJsonConverter());
        options.Converters.Add(new DrawObjectIdJsonConverter());
        options.Converters.Add(new RenderOutputIdJsonConverter());
        options.Converters.Add(new MediaSourceTypeIdJsonConverter());
        options.Converters.Add(new ColorRgbaJsonConverter());
        options.Converters.Add(new FrameSizeJsonConverter());
        options.Converters.Add(new Transform2DJsonConverter());
        options.Converters.Add(new NormalizedRectJsonConverter());

        return options;
    }
}
