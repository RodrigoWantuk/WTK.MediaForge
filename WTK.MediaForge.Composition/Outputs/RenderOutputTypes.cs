using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public static class RenderOutputTypes
{
    public static readonly RenderOutputTypeId PreviewWindow = new("wtk.output.preview.window");
    public static readonly RenderOutputTypeId Offscreen = new("wtk.output.offscreen");
    public static readonly RenderOutputTypeId Ndi = new("wtk.output.ndi");
    public static readonly RenderOutputTypeId RecordingMp4 = new("wtk.output.recording.mp4");
    public static readonly RenderOutputTypeId StreamingRtmp = new("wtk.output.streaming.rtmp");
    public static readonly RenderOutputTypeId VirtualCamera = new("wtk.output.virtual.camera");
}
