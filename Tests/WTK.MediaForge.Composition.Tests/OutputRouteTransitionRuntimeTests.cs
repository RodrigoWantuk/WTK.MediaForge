using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
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

    [Fact]
    public void Scene_version_transition_exposes_previous_and_current_graphs()
    {
        using var runtime = new OutputRouteTransitionRuntime();
        var outputId = RenderOutputId.New();
        var rootCanvasId = CanvasId.New();
        var nestedCanvasId = CanvasId.New();
        var oldNestedVersion = SceneVersionId.New();
        var newNestedVersion = SceneVersionId.New();
        var previousProjectState = new ProjectStateSnapshot();
        var ownership = new TrackingDisposable();

        runtime.BeginSceneVersionTransition(
            outputId,
            OutputRouteTransition.Fade("apply-fade", durationMs: 1_000),
            new SceneVersionGraph(
                rootCanvasId,
                new Dictionary<CanvasId, SceneVersionId>
                {
                    [nestedCanvasId] = oldNestedVersion
                }),
            new SceneVersionGraph(
                rootCanvasId,
                new Dictionary<CanvasId, SceneVersionId>
                {
                    [nestedCanvasId] = newNestedVersion
                }),
            previousProjectState,
            ownership);

        Assert.True(runtime.TryGetTransition(outputId, out var state));
        Assert.Equal(rootCanvasId, state.FromCanvasId);
        Assert.Equal(rootCanvasId, state.ToCanvasId);
        Assert.Same(previousProjectState, state.PreviousProjectState);
        Assert.Equal(oldNestedVersion, state.PreviousVersionGraph.CanvasVersions[nestedCanvasId]);
        Assert.Equal(newNestedVersion, state.CurrentVersionGraph.CanvasVersions[nestedCanvasId]);
        Assert.False(ownership.IsDisposed);

        runtime.Advance(outputId, TimeSpan.FromSeconds(1));

        Assert.True(ownership.IsDisposed);
    }

    [Fact]
    public void Replacing_scene_version_transition_releases_previous_ownership()
    {
        using var runtime = new OutputRouteTransitionRuntime();
        var outputId = RenderOutputId.New();
        var canvasId = CanvasId.New();
        var firstOwnership = new TrackingDisposable();
        var secondOwnership = new TrackingDisposable();
        var graph = new SceneVersionGraph(
            canvasId,
            new Dictionary<CanvasId, SceneVersionId> { [canvasId] = SceneVersionId.New() });

        runtime.BeginSceneVersionTransition(
            outputId,
            OutputRouteTransition.Fade("first", durationMs: 1_000),
            graph,
            graph,
            new ProjectStateSnapshot(),
            firstOwnership);
        runtime.BeginSceneVersionTransition(
            outputId,
            OutputRouteTransition.Fade("second", durationMs: 1_000),
            graph,
            graph,
            new ProjectStateSnapshot(),
            secondOwnership);

        Assert.True(firstOwnership.IsDisposed);
        Assert.False(secondOwnership.IsDisposed);

        runtime.Clear();

        Assert.True(secondOwnership.IsDisposed);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
