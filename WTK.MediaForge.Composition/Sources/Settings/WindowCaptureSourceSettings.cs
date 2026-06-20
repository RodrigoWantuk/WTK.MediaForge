using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources.Settings;

public sealed class WindowCaptureSourceSettings : IMediaSourceSettings
{
    public MediaSourceTypeId TypeId => MediaSourceTypes.WindowCapture;

    public int SchemaVersion { get; init; } = 1;

    public long WindowHandle { get; init; }

    public bool CaptureCursor { get; init; } = true;
}
