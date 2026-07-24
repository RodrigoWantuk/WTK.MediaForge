using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using Xunit;

namespace WTK.MediaForge.Core.Tests;

public class CapturePreviewGeometryTests
{
    [Fact]
    public void ResolveShaderRotation_portrait_monitor_with_swapped_duplication_texture_uses_dxgi_rotation()
    {
        var rotation = CapturePreviewGeometry.ResolveShaderRotation(
            DisplayRotation.Rotate90,
            logicalSize: new FrameSize(1024, 1280),
            textureSize: new FrameSize(1280, 1024));

        Assert.Equal((int)DisplayRotation.Rotate90, rotation);
    }

    [Fact]
    public void ResolveShaderRotation_portrait_monitor_when_metadata_sizes_match_still_uses_dxgi_rotation()
    {
        var rotation = CapturePreviewGeometry.ResolveShaderRotation(
            DisplayRotation.Rotate90,
            logicalSize: new FrameSize(1024, 1280),
            textureSize: new FrameSize(1024, 1280));

        Assert.Equal((int)DisplayRotation.Rotate90, rotation);
    }

    [Fact]
    public void ResolveShaderRotation_landscape_monitor_without_rotation_is_none()
    {
        var rotation = CapturePreviewGeometry.ResolveShaderRotation(
            DisplayRotation.None,
            logicalSize: new FrameSize(2560, 1080),
            textureSize: new FrameSize(2560, 1080));

        Assert.Equal((int)DisplayRotation.None, rotation);
    }

    [Fact]
    public void ResolveNativeTextureSize_swaps_when_mode_matches_logical_portrait_with_rotation()
    {
        var native = CaptureDuplicationSizes.ResolveNativeTextureSize(
            modeDescriptionSize: new FrameSize(1024, 1280),
            logicalDesktopSize: new FrameSize(1024, 1280),
            rotation: DisplayRotation.Rotate90);

        Assert.Equal(new FrameSize(1280, 1024), native);
    }

    [Fact]
    public void EstimateNativeTextureSize_swaps_for_rotated_outputs()
    {
        var native = CaptureDuplicationSizes.EstimateNativeTextureSize(
            new FrameSize(1024, 1280),
            DisplayRotation.Rotate270);

        Assert.Equal(new FrameSize(1280, 1024), native);
    }
}
