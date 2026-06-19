using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Core.Tests;

public class IdentifierTests
{
    [Fact]
    public void SourceId_From_rejects_empty_guid()
    {
        Assert.Throws<ArgumentException>(() => SourceId.From(Guid.Empty));
    }

    [Fact]
    public void SourceId_New_is_not_empty()
    {
        var id = SourceId.New();
        Assert.False(id.IsEmpty);
    }

    [Fact]
    public void MediaSourceTypeId_From_rejects_blank()
    {
        Assert.Throws<ArgumentException>(() => MediaSourceTypeId.From(" "));
    }

    [Fact]
    public void MediaSourceTypeId_built_in_ids_are_not_empty()
    {
        Assert.False(MediaSourceTypeId.DesktopCapture.IsEmpty);
        Assert.Equal("wtk.desktop.capture", MediaSourceTypeId.DesktopCapture.Value);
    }
}

public class NormalizedRectTests
{
    [Fact]
    public void Full_is_valid()
    {
        Assert.True(NormalizedRect.Full.IsValid);
    }

    [Fact]
    public void Invalid_rect_detected()
    {
        var invalid = new NormalizedRect(0.5f, 0, 0.25f, 1);
        Assert.False(invalid.IsValid);
    }
}

public class ColorRgbaTests
{
    [Fact]
    public void From_clamps_components()
    {
        var color = ColorRgba.From(2, -1, 0.5f, 1.5f);
        Assert.Equal(1f, color.R);
        Assert.Equal(0f, color.G);
        Assert.True(color.IsInRange());
    }

    [Fact]
    public void Black_is_opaque()
    {
        Assert.Equal(1f, ColorRgba.Black.A);
    }
}

public class Transform2DTests
{
    [Fact]
    public void HasPositiveSize_requires_both_dimensions()
    {
        var transform = new Transform2D
        {
            Size = new CanvasSize(640, 0)
        };

        Assert.False(transform.HasPositiveSize);
    }
}

public class GpuFrameLeaseTests
{
    [Fact]
    public void Dispose_is_idempotent()
    {
        var releaseCount = 0;
        var lease = GpuFrameLease.Create(default, () => releaseCount++);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void Dispose_continues_after_release_throws()
    {
        var lease = GpuFrameLease.Create(default, () => throw new InvalidOperationException("release failed"));

        lease.Dispose();
    }
}
