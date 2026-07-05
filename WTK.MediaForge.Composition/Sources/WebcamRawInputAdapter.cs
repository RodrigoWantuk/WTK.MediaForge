using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Sources;

/// <summary>
/// Webcam adapter that may receive system raw CPU frames and uploads immediately to GPU.
/// </summary>
[RawCpuVideoFrameException(RawCpuVideoFrameExceptionKind.WebcamSystemRawInput)]
public sealed class WebcamRawInputAdapter
{
    public bool PreferGpuNativePath { get; init; } = true;

    public bool RequiresImmediateGpuUpload { get; init; } = true;

    public MediaTransportKind TransportKind =>
        PreferGpuNativePath
            ? MediaTransportKind.GpuSurface
            : MediaTransportKind.RawCpuVideoFrameException;
}
