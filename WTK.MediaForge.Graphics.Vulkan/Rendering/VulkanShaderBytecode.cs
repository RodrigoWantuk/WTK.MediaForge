namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanShaderBytecode
{
    public static ReadOnlySpan<byte> CommonVertex => CompiledShaders.Shaders.Catalog.mf_common_vert;

    public static ReadOnlySpan<byte> SourceLayerFragment => CompiledShaders.Shaders.Catalog.mf_source_layer_frag;

    public static ReadOnlySpan<byte> SolidFragment => CompiledShaders.Shaders.Catalog.mf_solid_frag;

    public static ReadOnlySpan<byte> OutputLetterboxFragment => CompiledShaders.Shaders.Catalog.mf_output_letterbox_frag;
}
