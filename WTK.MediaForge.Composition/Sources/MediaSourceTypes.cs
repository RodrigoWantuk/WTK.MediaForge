using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources;

public static class MediaSourceTypes
{
    public static readonly MediaSourceTypeId Desktop = new("wtk.source.desktop");
    public static readonly MediaSourceTypeId Webcam = new("wtk.source.webcam");
    public static readonly MediaSourceTypeId NdiInput = new("wtk.source.ndi.input");
    public static readonly MediaSourceTypeId RtspInput = new("wtk.source.rtsp.input");
    public static readonly MediaSourceTypeId IpCamera = new("wtk.source.ip.camera");
    public static readonly MediaSourceTypeId VideoFile = new("wtk.source.video.file");
    public static readonly MediaSourceTypeId ImageFile = new("wtk.source.image.file");
    public static readonly MediaSourceTypeId AnimatedImage = new("wtk.source.image.animated");
    public static readonly MediaSourceTypeId Lottie = new("wtk.source.lottie");
    public static readonly MediaSourceTypeId WindowCapture = new("wtk.source.window.capture");
    public static readonly MediaSourceTypeId Generated = new("wtk.source.generated");
}
