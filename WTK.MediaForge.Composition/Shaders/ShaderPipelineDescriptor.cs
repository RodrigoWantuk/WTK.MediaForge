namespace WTK.MediaForge.Composition.Shaders;

public sealed class ShaderPipelineDescriptor
{
    public ShaderPipelineKind Kind { get; init; }

    /// <summary>Stable catalog id, e.g. mf.source.layer.</summary>
    public string CatalogId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string VertexShaderFileName { get; init; } = string.Empty;

    public string FragmentShaderFileName { get; init; } = string.Empty;

    public bool SamplesSourceTexture { get; init; }

    public bool SupportsBlendMode { get; init; } = true;

    public bool SupportsOpacity { get; init; } = true;
}
