using Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Docking;

public sealed record StudioMonitorWorkArea(string Id, Rect Bounds, bool IsPrimary = false);

internal static class StudioDockLayoutState
{
    private static readonly StudioMonitorWorkArea VirtualPrimary =
        new("virtual-primary", new Rect(0, 0, 1920, 1080), true);

    public static IReadOnlyList<StudioFloatingDockState> Capture(
        IRootDock root,
        IReadOnlyList<StudioMonitorWorkArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(root);
        var available = NormalizeMonitors(monitors);
        return (root.Windows ?? [])
            .Select(window => CaptureWindow(window, available))
            .Where(static state => state is not null)
            .Select(static state => state!)
            .GroupBy(static state => state.ToolId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    public static void Restore(
        IRootDock root,
        StudioDockFactory factory,
        IEnumerable<StudioFloatingDockState> states,
        IReadOnlyList<StudioMonitorWorkArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(states);
        var available = NormalizeMonitors(monitors);
        foreach (var state in states.GroupBy(static item => item.ToolId, StringComparer.Ordinal).Select(static group => group.First()))
        {
            var tool = EnumerateDockables(root).OfType<Tool>().FirstOrDefault(item => item.Id == state.ToolId);
            if (tool is null || IsAlreadyFloating(root, tool)) continue;

            var normalized = Normalize(state, available);
            factory.FloatDockable(tool, new DockWindowOptions
            {
                OwnerMode = DockWindowOwnerMode.None,
                ShowInTaskbar = true
            });
            var window = (root.Windows ?? []).FirstOrDefault(item => Contains(item.Layout, tool));
            if (window is null) continue;
            window.X = normalized.X;
            window.Y = normalized.Y;
            window.Width = normalized.Width;
            window.Height = normalized.Height;
            window.ShowInTaskbar = true;
        }
    }

    internal static StudioFloatingDockState Normalize(
        StudioFloatingDockState state,
        IReadOnlyList<StudioMonitorWorkArea> monitors)
    {
        var available = NormalizeMonitors(monitors);
        var monitor = available.FirstOrDefault(item => item.Id == state.MonitorId)
            ?? available.FirstOrDefault(static item => item.IsPrimary)
            ?? available[0];
        var width = ClampSize(state.Width, 280, Math.Max(280, monitor.Bounds.Width), 420);
        var height = ClampSize(state.Height, 220, Math.Max(220, monitor.Bounds.Height), 520);
        var x = ClampFinite(state.X, monitor.Bounds.Left, monitor.Bounds.Right - width, monitor.Bounds.Left + 80);
        var y = ClampFinite(state.Y, monitor.Bounds.Top, monitor.Bounds.Bottom - height, monitor.Bounds.Top + 80);
        return new StudioFloatingDockState
        {
            ToolId = state.ToolId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MonitorId = monitor.Id
        };
    }

    private static StudioFloatingDockState? CaptureWindow(IDockWindow window, IReadOnlyList<StudioMonitorWorkArea> monitors)
    {
        var tool = EnumerateDockables(window.Layout).OfType<Tool>().FirstOrDefault();
        if (tool?.Id is null) return null;
        var center = new Point(window.X + window.Width / 2, window.Y + window.Height / 2);
        var monitor = monitors.FirstOrDefault(item => item.Bounds.Contains(center))
            ?? monitors.FirstOrDefault(static item => item.IsPrimary)
            ?? monitors[0];
        return Normalize(new StudioFloatingDockState
        {
            ToolId = tool.Id,
            X = window.X,
            Y = window.Y,
            Width = window.Width,
            Height = window.Height,
            MonitorId = monitor.Id
        }, monitors);
    }

    private static bool IsAlreadyFloating(IRootDock root, Tool tool) =>
        (root.Windows ?? []).Any(window => Contains(window.Layout, tool));

    private static bool Contains(IDockable? root, IDockable target) =>
        EnumerateDockables(root).Any(item => ReferenceEquals(item, target));

    private static IEnumerable<IDockable> EnumerateDockables(IDockable? dockable)
    {
        if (dockable is null) yield break;
        yield return dockable;
        if (dockable is not IDock dock || dock.VisibleDockables is null) yield break;
        foreach (var child in dock.VisibleDockables)
            foreach (var descendant in EnumerateDockables(child))
                yield return descendant;
    }

    private static IReadOnlyList<StudioMonitorWorkArea> NormalizeMonitors(IReadOnlyList<StudioMonitorWorkArea>? monitors) =>
        monitors is { Count: > 0 } ? monitors : [VirtualPrimary];

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value) || maximum < minimum) return fallback;
        return Math.Clamp(value, minimum, maximum);
    }

    private static double ClampSize(double value, double minimum, double maximum, double fallback) =>
        !double.IsFinite(value) || value <= 0 ? fallback : Math.Clamp(value, minimum, maximum);
}
