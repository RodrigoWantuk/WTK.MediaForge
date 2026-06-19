using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Shaders;

public static class RenderDrawObjectPipelineMapper
{
    public static ShaderPipelineKind GetPipelineKind(RenderDrawObjectSnapshot drawObject) =>
        drawObject switch
        {
            RenderSourceLayerDrawObjectSnapshot => ShaderPipelineKind.SourceLayer,
            RenderSolidDrawObjectSnapshot => ShaderPipelineKind.Solid,
            RenderTextDrawObjectSnapshot => ShaderPipelineKind.Text,
            RenderCanvasDrawObjectSnapshot => ShaderPipelineKind.CanvasComposite,
            _ => ShaderPipelineKind.Unknown
        };

    public static ShaderPipelineKind GetPipelineKind(DrawObjectStateSnapshot drawObject) =>
        drawObject switch
        {
            SourceLayerDrawObjectSnapshot => ShaderPipelineKind.SourceLayer,
            SolidDrawObjectSnapshot => ShaderPipelineKind.Solid,
            TextDrawObjectSnapshot => ShaderPipelineKind.Text,
            CanvasDrawObjectSnapshot => ShaderPipelineKind.CanvasComposite,
            _ => ShaderPipelineKind.Unknown
        };

    public static ShaderPipelineKind GetPipelineKind(MediaForgeDrawObject drawObject) =>
        drawObject switch
        {
            SourceLayerDrawObject => ShaderPipelineKind.SourceLayer,
            SolidDrawObject => ShaderPipelineKind.Solid,
            TextDrawObject => ShaderPipelineKind.Text,
            CanvasDrawObject => ShaderPipelineKind.CanvasComposite,
            _ => ShaderPipelineKind.Unknown
        };

    public static ShaderPipelineDescriptor GetDescriptor(RenderDrawObjectSnapshot drawObject) =>
        ShaderPipelineCatalog.GetRequired(GetPipelineKind(drawObject));
}
