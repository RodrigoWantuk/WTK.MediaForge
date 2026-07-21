using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaSourceTypeCatalogTests
{
    [Fact]
    public void Registry_exposes_all_official_source_types()
    {
        var typeIds = MediaSourceTypeRegistry.All.Select(d => d.TypeId.Value).OrderBy(v => v).ToArray();

        Assert.Equal(
        [
            "remote-scene",
            "wtk.source.desktop",
            "wtk.source.generated",
            "wtk.source.image.animated",
            "wtk.source.image.file",
            "wtk.source.ip.camera",
            "wtk.source.lottie",
            "wtk.source.ndi.input",
            "wtk.source.rtsp.input",
            "wtk.source.video.file",
            "wtk.source.webcam",
            "wtk.source.window.capture"
        ], typeIds);
    }

    [Fact]
    public void Settings_serializer_round_trips_new_media_source_settings()
    {
        var animated = MediaForgeSources.AnimatedImage("overlay.webp", preferredFrameRate: 30);
        var animatedJson = MediaSourceSettingsSerializer.ToJson(animated);
        var restoredAnimated = Assert.IsType<AnimatedImageSourceSettings>(
            MediaSourceSettingsSerializer.Deserialize(MediaSourceTypes.AnimatedImage, animatedJson));
        Assert.Equal("overlay.webp", restoredAnimated.Path);
        Assert.Equal(30, restoredAnimated.PreferredFrameRate);

        var lottie = MediaForgeSources.Lottie("lower-third.json", loop: false);
        var lottieJson = MediaSourceSettingsSerializer.ToJson(lottie);
        var restoredLottie = Assert.IsType<LottieSourceSettings>(
            MediaSourceSettingsSerializer.Deserialize(MediaSourceTypes.Lottie, lottieJson));
        Assert.False(restoredLottie.Loop);

        var camera = MediaForgeSources.IpCamera("rtsp://camera/live", RtspTransportMode.Udp);
        var cameraJson = MediaSourceSettingsSerializer.ToJson(camera);
        var restoredCamera = Assert.IsType<IpCameraSourceSettings>(
            MediaSourceSettingsSerializer.Deserialize(MediaSourceTypes.IpCamera, cameraJson));
        Assert.Equal(RtspTransportMode.Udp, restoredCamera.Transport);
    }

    [Theory]
    [InlineData("wtk.desktop.capture", "wtk.source.desktop")]
    [InlineData("wtk.image.file", "wtk.source.image.file")]
    [InlineData("wtk.video.file", "wtk.source.video.file")]
    public void Legacy_type_ids_resolve_to_canonical(string legacy, string canonical)
    {
        var resolved = MediaSourceTypeRegistry.ResolveCanonical(new MediaSourceTypeId(legacy));
        Assert.Equal(canonical, resolved.Value);
    }

    [Fact]
    public void Settings_serializer_round_trips_desktop_capture_settings()
    {
        var settings = new DesktopCaptureSourceSettings
        {
            AdapterIndex = 1,
            OutputIndex = 2,
            CaptureCursor = false
        };

        var json = MediaSourceSettingsSerializer.ToJson(settings);
        var restored = MediaSourceSettingsSerializer.Deserialize(MediaSourceTypes.Desktop, json);

        var desktop = Assert.IsType<DesktopCaptureSourceSettings>(restored);
        Assert.Equal(1, desktop.AdapterIndex);
        Assert.Equal(2, desktop.OutputIndex);
        Assert.False(desktop.CaptureCursor);
    }

    [Fact]
    public void Migrator_normalizes_legacy_source_type_ids()
    {
        var project = new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Name = "Desktop",
                    TypeId = new MediaSourceTypeId("wtk.desktop.capture"),
                    Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings())
                }
            ]
        };

        var result = MediaForgeProjectMigrator.Migrate(project);
        Assert.True(result.Success);
        Assert.Equal(MediaSourceTypes.Desktop, project.SourceDefinitions[0].TypeId);
    }

    [Fact]
    public void Validator_rejects_webcam_without_device_id()
    {
        var project = new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Name = "Cam",
                    TypeId = MediaSourceTypes.Webcam,
                    Settings = MediaSourceSettingsSerializer.ToJson(new WebcamSourceSettings())
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "source.webcam.device");
    }

    [Fact]
    public void Validator_accepts_minimal_valid_desktop_source()
    {
        var project = new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Name = "Desktop",
                    TypeId = MediaSourceTypes.Desktop,
                    Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings())
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Unknown_source_type_fails_validation()
    {
        var project = new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Name = "Mystery",
                    TypeId = new MediaSourceTypeId("wtk.source.unknown")
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "source.type.invalid");
    }
}
