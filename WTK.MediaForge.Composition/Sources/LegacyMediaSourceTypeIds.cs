using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources;

internal static class LegacyMediaSourceTypeIds
{
    public static readonly MediaSourceTypeId DesktopCapture = new("wtk.desktop.capture");
    public static readonly MediaSourceTypeId ImageFile = new("wtk.image.file");
    public static readonly MediaSourceTypeId VideoFile = new("wtk.video.file");
}
