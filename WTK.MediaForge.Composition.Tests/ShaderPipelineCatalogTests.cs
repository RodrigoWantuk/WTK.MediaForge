using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Shaders;
using WTK.MediaForge.Composition.Snapshots;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class ShaderPipelineCatalogTests
{
    [Fact]
    public void Catalog_ids_are_unique_and_prefixed_with_mf()
    {
        var ids = ShaderPipelineCatalog.All.Select(p => p.CatalogId).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith("mf.", id));
    }

    [Theory]
    [InlineData(typeof(SourceLayerDrawObject), ShaderPipelineKind.SourceLayer, "mf.source.layer")]
    [InlineData(typeof(SolidDrawObject), ShaderPipelineKind.Solid, "mf.solid")]
    [InlineData(typeof(TextDrawObject), ShaderPipelineKind.Text, "mf.text")]
    [InlineData(typeof(CanvasDrawObject), ShaderPipelineKind.CanvasComposite, "mf.canvas.composite")]
    public void Mapper_resolves_project_draw_objects(Type drawObjectType, ShaderPipelineKind expectedKind, string expectedCatalogId)
    {
        var drawObject = (MediaForgeDrawObject)Activator.CreateInstance(drawObjectType)!;

        var kind = RenderDrawObjectPipelineMapper.GetPipelineKind(drawObject);
        var descriptor = ShaderPipelineCatalog.GetRequired(kind);

        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedCatalogId, descriptor.CatalogId);
    }

    [Fact]
    public void Mapper_resolves_render_snapshot_draw_objects()
    {
        Assert.Equal(
            ShaderPipelineKind.SourceLayer,
            RenderDrawObjectPipelineMapper.GetPipelineKind(new RenderSourceLayerDrawObjectSnapshot()));

        Assert.Equal(
            ShaderPipelineKind.Solid,
            RenderDrawObjectPipelineMapper.GetPipelineKind(new RenderSolidDrawObjectSnapshot()));

        Assert.Equal(
            ShaderPipelineKind.Text,
            RenderDrawObjectPipelineMapper.GetPipelineKind(new RenderTextDrawObjectSnapshot()));

        Assert.Equal(
            ShaderPipelineKind.CanvasComposite,
            RenderDrawObjectPipelineMapper.GetPipelineKind(new RenderCanvasDrawObjectSnapshot()));
    }

    [Fact]
    public void Output_letterbox_pipeline_is_available_for_render_targets()
    {
        var descriptor = ShaderPipelineCatalog.GetRequired(ShaderPipelineKind.OutputLetterbox);

        Assert.Equal("mf.output.letterbox", descriptor.CatalogId);
        Assert.Equal("mf_output_letterbox.frag", descriptor.FragmentShaderFileName);
    }
}
