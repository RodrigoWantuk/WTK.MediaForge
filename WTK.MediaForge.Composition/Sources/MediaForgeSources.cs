using WTK.MediaForge.Composition.Sources.Settings;

namespace WTK.MediaForge.Composition.Sources;

public static class MediaForgeSources
{
    public static DesktopCaptureSourceSettings Desktop(
        int adapterIndex = 0,
        int outputIndex = 0,
        bool captureCursor = true) =>
        new()
        {
            AdapterIndex = adapterIndex,
            OutputIndex = outputIndex,
            CaptureCursor = captureCursor
        };

    public static WindowCaptureSourceSettings Window(long windowHandle) =>
        new()
        {
            WindowHandle = windowHandle
        };

    public static WebcamSourceSettings Webcam(
        string deviceId,
        int? preferredWidth = null,
        int? preferredHeight = null,
        double? preferredFrameRate = null) =>
        new()
        {
            DeviceId = deviceId,
            PreferredWidth = preferredWidth,
            PreferredHeight = preferredHeight,
            PreferredFrameRate = preferredFrameRate
        };

    public static ImageFileSourceSettings Image(string path) =>
        new()
        {
            Path = path
        };

    public static AnimatedImageSourceSettings AnimatedImage(
        string path,
        bool loop = true,
        double? preferredFrameRate = null) =>
        new()
        {
            Path = path,
            Loop = loop,
            PreferredFrameRate = preferredFrameRate
        };

    public static LottieSourceSettings Lottie(
        string path,
        bool loop = true,
        double? preferredFrameRate = null) =>
        new()
        {
            Path = path,
            Loop = loop,
            PreferredFrameRate = preferredFrameRate
        };

    public static VideoFileSourceSettings MediaFile(
        string path,
        bool loop = true) =>
        new()
        {
            Path = path,
            Loop = loop
        };

    public static RtspInputSourceSettings Rtsp(
        string url,
        RtspTransportMode transport = RtspTransportMode.Tcp) =>
        new()
        {
            Url = url,
            Transport = transport
        };

    public static IpCameraSourceSettings IpCamera(
        string url,
        RtspTransportMode transport = RtspTransportMode.Tcp) =>
        new()
        {
            Url = url,
            Transport = transport
        };

    public static NdiInputSourceSettings Ndi(string sourceName) =>
        new()
        {
            SourceName = sourceName
        };

    public static GeneratedSourceSettings Generated(string generatorKind) =>
        new()
        {
            GeneratorKind = generatorKind
        };

    public static RemoteSceneSourceSettings RemoteScene(string signalingEndpoint, string streamName) =>
        new()
        {
            SignalingEndpoint = signalingEndpoint,
            StreamName = streamName
        };
}
