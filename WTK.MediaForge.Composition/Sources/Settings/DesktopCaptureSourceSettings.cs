using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class DesktopCaptureSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.Desktop;

    public int SchemaVersion { get; init; } = 1;

    public int AdapterIndex { get; init; }

    public int OutputIndex { get; init; }

    public bool CaptureCursor { get; init; } = true;

    public bool CaptureAudio { get; init; }
}
