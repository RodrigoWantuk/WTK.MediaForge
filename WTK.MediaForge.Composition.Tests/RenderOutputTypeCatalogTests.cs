using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputTypeCatalogTests
{
    [Fact]
    public void Registry_exposes_all_official_output_types()
    {
        var typeIds = RenderOutputTypeRegistry.All.Select(d => d.TypeId.Value).OrderBy(v => v).ToArray();

        Assert.Equal(
        [
            "wtk.output.ndi",
            "wtk.output.offscreen",
            "wtk.output.preview.window",
            "wtk.output.recording.mp4",
            "wtk.output.streaming.rtmp",
            "wtk.output.virtual.camera"
        ], typeIds);
    }

    [Fact]
    public void Settings_serializer_round_trips_preview_window_settings()
    {
        var settings = new PreviewWindowOutputSettings
        {
            Title = "Program",
            EnableVSync = false
        };

        var json = RenderOutputSettingsSerializer.ToJson(settings);
        var restored = RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.PreviewWindow, json);

        var preview = Assert.IsType<PreviewWindowOutputSettings>(restored);
        Assert.Equal("Program", preview.Title);
        Assert.False(preview.EnableVSync);
    }

    [Fact]
    public void Migrator_defaults_empty_output_type_to_preview_window()
    {
        var project = new MediaForgeProject
        {
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    Name = "Out",
                    TypeId = default
                }
            ]
        };

        var result = MediaForgeProjectMigrator.Migrate(project);
        Assert.True(result.Success);
        Assert.Equal(RenderOutputTypes.PreviewWindow, project.Outputs[0].TypeId);
    }

    [Fact]
    public void Validator_rejects_rtmp_without_url()
    {
        var project = new MediaForgeProject
        {
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    Name = "Stream",
                    TypeId = RenderOutputTypes.StreamingRtmp,
                    Settings = RenderOutputSettingsSerializer.ToJson(new StreamingRtmpOutputSettings())
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "output.rtmp.url");
    }
}
