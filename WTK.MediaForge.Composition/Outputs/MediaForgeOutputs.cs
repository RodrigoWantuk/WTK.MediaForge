using WTK.MediaForge.Composition.Outputs.Settings;

namespace WTK.MediaForge.Composition.Outputs;

public static class MediaForgeOutputs
{
    public static OffscreenOutputSettings Offscreen() => new();

    public static PreviewWindowOutputSettings PreviewWindow(
        string title = "Preview",
        bool enableVSync = true) =>
        new()
        {
            Title = title,
            EnableVSync = enableVSync
        };

    public static RecordingMp4OutputSettings RecordMp4(
        string path,
        EncodedVideoProfile? video = null) =>
        new()
        {
            Path = path,
            Video = video ?? EncodedVideoProfile.DefaultH264
        };

    public static EncodedFileOutputSettings EncodedFile(
        string path,
        string container = "mp4",
        string videoCodec = "h264",
        string audioCodec = "aac") =>
        new()
        {
            Path = path,
            Container = container,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec
        };

    public static StreamingRtmpOutputSettings Rtmp(
        string url,
        string streamKey,
        EncodedVideoProfile? video = null) =>
        new()
        {
            Url = url,
            StreamKey = streamKey,
            Video = video ?? EncodedVideoProfile.DefaultH264
        };

    public static StreamingSrtOutputSettings Srt(string url) =>
        new()
        {
            Url = url
        };

    public static StreamingRtspOutputSettings Rtsp(string url) =>
        new()
        {
            Url = url
        };

    public static StreamingHlsOutputSettings Hls(string path) =>
        new()
        {
            Path = path
        };

    public static NdiOutputSettings Ndi(string sourceName) =>
        new()
        {
            SourceName = sourceName
        };

    public static VirtualCameraOutputSettings VirtualCamera(string deviceName) =>
        new()
        {
            DeviceName = deviceName
        };

    public static RemoteSceneOutputSettings RemoteScene(
        string signalingEndpoint,
        string streamName,
        EncodedVideoProfile? video = null) =>
        new()
        {
            SignalingEndpoint = signalingEndpoint,
            StreamName = streamName,
            Video = video ?? EncodedVideoProfile.DefaultH264
        };
}
