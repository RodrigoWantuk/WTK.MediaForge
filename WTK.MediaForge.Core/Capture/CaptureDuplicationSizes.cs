using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Capture;

public static class CaptureDuplicationSizes
{
    /// <summary>
    /// Desktop Duplication returns an unrotated surface. When DXGI reports rotation,
    /// ModeDescription may still match the logical desktop size (e.g. 1024×1280 portrait).
    /// The native duplication texture is then the swapped dimensions (1280×1024).
    /// </summary>
    public static FrameSize ResolveNativeTextureSize(
        FrameSize modeDescriptionSize,
        FrameSize logicalDesktopSize,
        DisplayRotation rotation)
    {
        if (modeDescriptionSize.Width == 0 || modeDescriptionSize.Height == 0)
            return modeDescriptionSize;

        if (logicalDesktopSize.Width == 0 || logicalDesktopSize.Height == 0)
            return modeDescriptionSize;

        if (rotation is not (DisplayRotation.Rotate90 or DisplayRotation.Rotate270))
            return modeDescriptionSize;

        var swappedLogical = new FrameSize(logicalDesktopSize.Height, logicalDesktopSize.Width);
        if (modeDescriptionSize == logicalDesktopSize && modeDescriptionSize != swappedLogical)
            return swappedLogical;

        return modeDescriptionSize;
    }

    public static FrameSize EstimateNativeTextureSize(FrameSize logicalDesktopSize, DisplayRotation rotation)
    {
        if (logicalDesktopSize.Width == 0 || logicalDesktopSize.Height == 0)
            return logicalDesktopSize;

        return rotation is DisplayRotation.Rotate90 or DisplayRotation.Rotate270
            ? new FrameSize(logicalDesktopSize.Height, logicalDesktopSize.Width)
            : logicalDesktopSize;
    }
}
