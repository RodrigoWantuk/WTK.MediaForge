using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public sealed class OutputRouteTransitionRuntimeTests
{
    [Fact]
    public void Fade_progress_uses_supplied_delta_time_only()
    {
        using var runtime = new OutputRouteTransitionRuntime();
        var outputId = RenderOutputId.New();

        runtime.BeginTransition(
            outputId,
            OutputRouteTransition.Fade("fade", durationMs: 1_000),
            CanvasId.New(),
            CanvasId.New());

        runtime.Advance(outputId, TimeSpan.FromMilliseconds(250));
        Assert.True(runtime.TryGetProgress(outputId, out var firstProgress));
        Assert.Equal(0.25f, firstProgress, precision: 3);

        Thread.Sleep(25);
        Assert.True(runtime.TryGetProgress(outputId, out var unchangedProgress));
        Assert.Equal(firstProgress, unchangedProgress, precision: 3);

        runtime.Advance(outputId, TimeSpan.FromMilliseconds(250));
        Assert.True(runtime.TryGetProgress(outputId, out var secondProgress));
        Assert.Equal(0.5f, secondProgress, precision: 3);
    }

    [Fact]
    public void Fade_ignores_negative_delta_time()
    {
        using var runtime = new OutputRouteTransitionRuntime();
        var outputId = RenderOutputId.New();

        runtime.BeginTransition(
            outputId,
            OutputRouteTransition.Fade("fade", durationMs: 1_000),
            CanvasId.New(),
            CanvasId.New());

        runtime.Advance(outputId, TimeSpan.FromMilliseconds(250));
        runtime.Advance(outputId, TimeSpan.FromMilliseconds(-500));

        Assert.True(runtime.TryGetProgress(outputId, out var progress));
        Assert.Equal(0.25f, progress, precision: 3);
    }

    [Fact]
    public void Completed_fade_removes_active_transition()
    {
        using var runtime = new OutputRouteTransitionRuntime();
        var outputId = RenderOutputId.New();

        runtime.BeginTransition(
            outputId,
            OutputRouteTransition.Fade("fade", durationMs: 1_000),
            CanvasId.New(),
            CanvasId.New());

        runtime.Advance(outputId, TimeSpan.FromMilliseconds(1_000));

        Assert.False(runtime.TryGetProgress(outputId, out _));
    }
}
