using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public static class CapturePreviewGeometry
{
    public static int ResolveShaderRotation(
        DisplayRotation reportedRotation,
        FrameSize logicalSize,
        FrameSize textureSize)
    {
        if (logicalSize.Width == 0 || logicalSize.Height == 0)
            return (int)DisplayRotation.None;

        if (textureSize.Width == 0 || textureSize.Height == 0)
        {
            return reportedRotation != DisplayRotation.None
                ? (int)reportedRotation
                : (int)DisplayRotation.None;
        }

        bool logicalPortrait = logicalSize.Height > logicalSize.Width;
        bool texturePortrait = textureSize.Height > textureSize.Width;
        bool orientationsDiffer = logicalPortrait != texturePortrait;
        bool dimensionsSwapped =
            logicalSize.Width == textureSize.Height &&
            logicalSize.Height == textureSize.Width;

        if (orientationsDiffer || dimensionsSwapped)
        {
            if (reportedRotation != DisplayRotation.None)
                return (int)reportedRotation;

            return logicalPortrait
                ? (int)DisplayRotation.Rotate90
                : (int)DisplayRotation.Rotate270;
        }

        // Metadata can report logical==texture while DXGI rotation still applies to duplication pixels.
        if (reportedRotation != DisplayRotation.None)
            return (int)reportedRotation;

        return (int)DisplayRotation.None;
    }
}
