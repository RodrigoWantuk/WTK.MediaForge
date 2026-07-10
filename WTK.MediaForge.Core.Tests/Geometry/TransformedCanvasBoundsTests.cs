using WTK.MediaForge.Core.Geometry;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Geometry;

public sealed class TransformedCanvasBoundsTests
{
    [Fact]
    public void TryCreate_without_rotation_uses_transform_bounds()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(10, 20),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.Center
        };

        Assert.True(TransformedCanvasBounds.TryCreate(transform, out var bounds));

        AssertBounds(bounds, 10, 20, 100, 50);
        AssertPoint(bounds.LocalOrigin, 0, 0);
    }

    [Fact]
    public void TryCreate_rotates_around_center_pivot()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(10, 20),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.Center,
            RotationDegrees = 90
        };

        Assert.True(TransformedCanvasBounds.TryCreate(transform, out var bounds));

        AssertBounds(bounds, 35, -5, 50, 100);
        AssertPoint(bounds.LocalOrigin, 25, -25);
    }

    [Fact]
    public void TryCreate_rotates_around_top_left_pivot()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(10, 20),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.TopLeft,
            RotationDegrees = 90
        };

        Assert.True(TransformedCanvasBounds.TryCreate(transform, out var bounds));

        AssertBounds(bounds, -40, 20, 50, 100);
        AssertPoint(bounds.LocalOrigin, -50, 0);
    }

    [Fact]
    public void TryCreate_rejects_invalid_transform()
    {
        var transform = new Transform2D
        {
            Position = new CanvasPoint(float.NaN, 20),
            Size = new CanvasSize(100, 50),
            Pivot = NormalizedPoint.Center
        };

        Assert.False(TransformedCanvasBounds.TryCreate(transform, out _));
    }

    private static void AssertBounds(
        TransformedCanvasBounds bounds,
        float x,
        float y,
        float width,
        float height)
    {
        Assert.Equal(x, bounds.Bounds.X, precision: 3);
        Assert.Equal(y, bounds.Bounds.Y, precision: 3);
        Assert.Equal(width, bounds.Bounds.Width, precision: 3);
        Assert.Equal(height, bounds.Bounds.Height, precision: 3);
        Assert.Equal(width, bounds.Size.Width, precision: 3);
        Assert.Equal(height, bounds.Size.Height, precision: 3);
    }

    private static void AssertPoint(CanvasPoint point, float x, float y)
    {
        Assert.Equal(x, point.X, precision: 3);
        Assert.Equal(y, point.Y, precision: 3);
    }
}
