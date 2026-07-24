using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;
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
            "remote-scene",
            "wtk.output.file.encoded",
            "wtk.output.ndi",
            "wtk.output.offscreen",
            "wtk.output.preview.window",
            "wtk.output.recording.mp4",
            "wtk.output.streaming.hls",
            "wtk.output.streaming.rtmp",
            "wtk.output.streaming.rtsp",
            "wtk.output.streaming.srt",
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
    public void Settings_serializer_round_trips_new_output_settings()
    {
        var encoded = MediaForgeOutputs.EncodedFile("program.mov", container: "mov", videoCodec: "prores");
        var encodedJson = RenderOutputSettingsSerializer.ToJson(encoded);
        var restoredEncoded = Assert.IsType<EncodedFileOutputSettings>(
            RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.EncodedFile, encodedJson));
        Assert.Equal("mov", restoredEncoded.Container);
        Assert.Equal("prores", restoredEncoded.VideoCodec);

        var srt = MediaForgeOutputs.Srt("srt://example.test:9000");
        var srtJson = RenderOutputSettingsSerializer.ToJson(srt);
        var restoredSrt = Assert.IsType<StreamingSrtOutputSettings>(
            RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.StreamingSrt, srtJson));
        Assert.Equal("srt://example.test:9000", restoredSrt.Url);
    }

    [Fact]
    public void Settings_serializer_round_trips_encoded_video_profiles()
    {
        var profile = new EncodedVideoProfile
        {
            FramesPerSecond = 30,
            BitrateBitsPerSecond = 5_500_000,
            KeyFrameIntervalFrames = 60,
            PixelFormat = "NV12",
            H264Profile = H264Profile.Main,
            H264Level = H264Level.Level41
        };

        var recordingJson = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("program.mp4", profile));
        var recording = Assert.IsType<RecordingMp4OutputSettings>(
            RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.RecordingMp4, recordingJson));
        Assert.Equal(30, recording.Video.FramesPerSecond);
        Assert.Equal(5_500_000, recording.Video.BitrateBitsPerSecond);
        Assert.Equal(60, recording.Video.KeyFrameIntervalFrames);
        Assert.Equal(H264Profile.Main, recording.Video.H264Profile);

        var rtmpJson = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp("rtmp://localhost/live", "program", profile));
        var rtmp = Assert.IsType<StreamingRtmpOutputSettings>(
            RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.StreamingRtmp, rtmpJson));
        Assert.Equal(30, rtmp.Video.FramesPerSecond);
        Assert.Equal(5_500_000, rtmp.Video.BitrateBitsPerSecond);
        Assert.Equal(H264Level.Level41, rtmp.Video.H264Level);
        Assert.Equal("Main", recordingJson["video"]!["h264Profile"]!.GetValue<string>());
        Assert.Equal("4.1", recordingJson["video"]!["h264Level"]!.GetValue<string>());
    }

    [Fact]
    public void Settings_serializer_reads_legacy_h264_profile_and_level_strings()
    {
        var json = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("program.mp4"));
        json["video"]!["h264Profile"] = "high";
        json["video"]!["h264Level"] = "4.2";

        var restored = Assert.IsType<RecordingMp4OutputSettings>(
            RenderOutputSettingsSerializer.Deserialize(RenderOutputTypes.RecordingMp4, json));

        Assert.Equal(H264Profile.High, restored.Video.H264Profile);
        Assert.Equal(H264Level.Level42, restored.Video.H264Level);
    }

    [Theory]
    [InlineData("100", "4.2")]
    [InlineData("High", "42")]
    public void Settings_serializer_rejects_numeric_h264_strings(string profile, string level)
    {
        var json = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("program.mp4"));
        json["video"]!["h264Profile"] = profile;
        json["video"]!["h264Level"] = level;

        Assert.False(RenderOutputSettingsSerializer.TryDeserialize(
            RenderOutputTypes.RecordingMp4,
            json,
            out _,
            out var issue));
        Assert.Equal("output.settings.invalid", issue!.Code);
    }

    [Fact]
    public void Validator_rejects_invalid_encoded_video_profile()
    {
        var project = new MediaForgeProject
        {
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    Name = "Recording",
                    TypeId = RenderOutputTypes.RecordingMp4,
                    Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4(
                        "program.mp4",
                        new EncodedVideoProfile
                        {
                            FramesPerSecond = 0,
                            BitrateBitsPerSecond = 8_000_000,
                            KeyFrameIntervalFrames = 120
                        }))
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Code == "output.video_profile.fps");
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

    [Fact]
    public void Output_capabilities_mark_media_outputs_unavailable_until_product_proofs_pass()
    {
        var entries = RenderOutputTypeRegistry.CreateCapabilityEntries();

        var offscreen = Assert.Single(entries, entry => entry.Id == $"output.{RenderOutputTypes.Offscreen.Value}");
        Assert.Equal(MediaForgeSupportStatus.Supported, offscreen.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, offscreen.ProductReadinessStatus);
        Assert.Equal(MediaTransportKind.GpuSurface, offscreen.TransportKind);

        var mp4 = Assert.Single(entries, entry => entry.Id == $"output.{RenderOutputTypes.RecordingMp4.Value}");
        Assert.Equal(MediaForgeSupportStatus.Unavailable, mp4.SupportStatus);
        Assert.Equal(MediaTransportKind.EncodedPacket, mp4.TransportKind);
        Assert.Contains("proofs", mp4.UnavailableReason, StringComparison.OrdinalIgnoreCase);

        var rtmp = Assert.Single(entries, entry => entry.Id == $"output.{RenderOutputTypes.StreamingRtmp.Value}");
        Assert.Equal(MediaForgeSupportStatus.Unavailable, rtmp.SupportStatus);
        Assert.Equal(MediaTransportKind.EncodedPacket, rtmp.TransportKind);
        Assert.Contains("RTMP", rtmp.UnavailableReason, StringComparison.OrdinalIgnoreCase);

        var ndi = Assert.Single(entries, entry => entry.Id == $"output.{RenderOutputTypes.Ndi.Value}");
        Assert.Equal(MediaForgeSupportStatus.Unsupported, ndi.SupportStatus);
        Assert.Equal(MediaForgeLicenseStatus.Approved, ndi.LicenseStatus);
        Assert.Contains("GPU-safe", ndi.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }
}
