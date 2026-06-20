namespace WTK.MediaForge.Composition.Shaders;

internal static class ShaderPipelineCatalog
{
    public static readonly ShaderPipelineDescriptor SourceLayer = new()
    {
        Kind = ShaderPipelineKind.SourceLayer,
        CatalogId = "mf.source.layer",
        DisplayName = "Source Layer",
        VertexShaderFileName = "mf_common.vert",
        FragmentShaderFileName = "mf_source_layer.frag",
        SamplesSourceTexture = true
    };

    public static readonly ShaderPipelineDescriptor Solid = new()
    {
        Kind = ShaderPipelineKind.Solid,
        CatalogId = "mf.solid",
        DisplayName = "Solid Fill",
        VertexShaderFileName = "mf_common.vert",
        FragmentShaderFileName = "mf_solid.frag",
        SamplesSourceTexture = false
    };

    public static readonly ShaderPipelineDescriptor Text = new()
    {
        Kind = ShaderPipelineKind.Text,
        CatalogId = "mf.text",
        DisplayName = "Text Overlay",
        VertexShaderFileName = "mf_common.vert",
        FragmentShaderFileName = "mf_text.frag",
        SamplesSourceTexture = true
    };

    public static readonly ShaderPipelineDescriptor CanvasComposite = new()
    {
        Kind = ShaderPipelineKind.CanvasComposite,
        CatalogId = "mf.canvas.composite",
        DisplayName = "Nested Canvas",
        VertexShaderFileName = "mf_common.vert",
        FragmentShaderFileName = "mf_canvas_composite.frag",
        SamplesSourceTexture = true
    };

    public static readonly ShaderPipelineDescriptor OutputLetterbox = new()
    {
        Kind = ShaderPipelineKind.OutputLetterbox,
        CatalogId = "mf.output.letterbox",
        DisplayName = "Output Letterbox",
        VertexShaderFileName = "mf_common.vert",
        FragmentShaderFileName = "mf_output_letterbox.frag",
        SamplesSourceTexture = true,
        SupportsBlendMode = false,
        SupportsOpacity = false
    };

    private static readonly IReadOnlyDictionary<ShaderPipelineKind, ShaderPipelineDescriptor> ByKind =
        new Dictionary<ShaderPipelineKind, ShaderPipelineDescriptor>
        {
            [ShaderPipelineKind.SourceLayer] = SourceLayer,
            [ShaderPipelineKind.Solid] = Solid,
            [ShaderPipelineKind.Text] = Text,
            [ShaderPipelineKind.CanvasComposite] = CanvasComposite,
            [ShaderPipelineKind.OutputLetterbox] = OutputLetterbox
        };

    public static IReadOnlyCollection<ShaderPipelineDescriptor> All => ByKind.Values.ToArray();

    public static ShaderPipelineDescriptor GetRequired(ShaderPipelineKind kind) =>
        ByKind.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Unknown shader pipeline kind: {kind}.");

    public static bool TryGet(ShaderPipelineKind kind, out ShaderPipelineDescriptor descriptor) =>
        ByKind.TryGetValue(kind, out descriptor!);
}
