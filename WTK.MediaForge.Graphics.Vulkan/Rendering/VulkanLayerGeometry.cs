using System.Numerics;
using Silk.NET.Vulkan;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal readonly struct VulkanLayerGeometry
{
    private VulkanLayerGeometry(
        Viewport viewport,
        Rect2D scissor,
        Vector4 geometryRect)
    {
        Viewport = viewport;
        Scissor = scissor;
        GeometryRect = geometryRect;
    }

    public Viewport Viewport { get; }

    public Rect2D Scissor { get; }

    public Vector4 GeometryRect { get; }

    public static bool TryCreate(
        Transform2D transform,
        FrameSize canvasSize,
        out VulkanLayerGeometry geometry)
    {
        geometry = default;

        if (!TransformedCanvasBounds.TryCreate(transform, out var bounds))
            return false;

        var left = Math.Max(0, (int)Math.Floor(bounds.Bounds.X));
        var top = Math.Max(0, (int)Math.Floor(bounds.Bounds.Y));
        var right = Math.Min(
            (int)canvasSize.Width,
            (int)Math.Ceiling(bounds.Bounds.X + bounds.Bounds.Width));
        var bottom = Math.Min(
            (int)canvasSize.Height,
            (int)Math.Ceiling(bounds.Bounds.Y + bounds.Bounds.Height));

        if (right <= left || bottom <= top)
            return false;

        geometry = new VulkanLayerGeometry(
            new Viewport
            {
                X = bounds.Bounds.X,
                Y = bounds.Bounds.Y,
                Width = bounds.Bounds.Width,
                Height = bounds.Bounds.Height,
                MinDepth = 0,
                MaxDepth = 1
            },
            new Rect2D
            {
                Offset = new Offset2D(left, top),
                Extent = new Extent2D((uint)(right - left), (uint)(bottom - top))
            },
            new Vector4(
                bounds.LocalOrigin.X,
                bounds.LocalOrigin.Y,
                bounds.Bounds.Width,
                bounds.Bounds.Height));

        return true;
    }
}
