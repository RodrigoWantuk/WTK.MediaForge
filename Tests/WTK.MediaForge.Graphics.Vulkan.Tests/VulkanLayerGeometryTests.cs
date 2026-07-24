using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public sealed class VulkanLayerGeometryTests
{
    [Fact]
    public void TryCreate_uses_rotated_bounds_for_viewport_and_clipped_scissor()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(10, 20),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.Center,
            RotationDegrees = 90
        };

        Assert.True(VulkanLayerGeometry.TryCreate(transform, new FrameSize(64, 64), out var geometry));

        Assert.Equal(35, geometry.Viewport.X, precision: 3);
        Assert.Equal(-5, geometry.Viewport.Y, precision: 3);
        Assert.Equal(50, geometry.Viewport.Width, precision: 3);
        Assert.Equal(100, geometry.Viewport.Height, precision: 3);

        Assert.Equal(34, geometry.Scissor.Offset.X);
        Assert.Equal(0, geometry.Scissor.Offset.Y);
        Assert.Equal(30u, geometry.Scissor.Extent.Width);
        Assert.Equal(64u, geometry.Scissor.Extent.Height);

        Assert.Equal(25, geometry.GeometryRect.X, precision: 3);
        Assert.Equal(-25, geometry.GeometryRect.Y, precision: 3);
        Assert.Equal(50, geometry.GeometryRect.Z, precision: 3);
        Assert.Equal(100, geometry.GeometryRect.W, precision: 3);
    }

    [Fact]
    public void TryCreate_rejects_layer_fully_outside_canvas()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(200, 200),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.Center
        };

        Assert.False(VulkanLayerGeometry.TryCreate(transform, new FrameSize(64, 64), out _));
    }
}
