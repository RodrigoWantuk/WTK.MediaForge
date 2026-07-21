using Avalonia;
using WTK.MediaForge.Studio.Docking;
using WTK.MediaForge.Studio.Models;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioDockLayoutStateTests
{
    [Fact]
    public void Missing_monitor_falls_back_to_primary_and_clamps_bounds()
    {
        var state = new StudioFloatingDockState
        {
            ToolId = "tool.navigation",
            MonitorId = "disconnected",
            X = 9_999,
            Y = -9_999,
            Width = 5_000,
            Height = 5_000
        };
        var monitors = new[] { new StudioMonitorWorkArea("primary", new Rect(100, 50, 1280, 720), true) };

        var result = StudioDockLayoutState.Normalize(state, monitors);

        Assert.Equal("primary", result.MonitorId);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(100, result.X);
        Assert.Equal(50, result.Y);
    }

    [Fact]
    public void Invalid_bounds_use_safe_visible_defaults()
    {
        var result = StudioDockLayoutState.Normalize(new StudioFloatingDockState
        {
            ToolId = "tool.properties",
            X = double.NaN,
            Y = double.PositiveInfinity,
            Width = -1,
            Height = -1
        }, []);

        Assert.Equal("virtual-primary", result.MonitorId);
        Assert.Equal(420, result.Width);
        Assert.Equal(520, result.Height);
        Assert.True(result.X >= 0 && result.Y >= 0);
    }
}
