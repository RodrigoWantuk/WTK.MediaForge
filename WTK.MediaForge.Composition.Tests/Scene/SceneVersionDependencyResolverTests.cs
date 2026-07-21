using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scene;

public sealed class SceneVersionDependencyResolverTests
{
    [Fact]
    public void Explicit_historical_parent_transitively_pins_child_until_root_is_released()
    {
        var childId = CanvasId.New();
        var parentId = CanvasId.New();
        var nestedLayerId = DrawObjectId.New();
        var outputId = RenderOutputId.New();
        var runtime = new SceneRuntime();

        runtime.SyncFrom(CreateState(
            childId, parentId, nestedLayerId, outputId,
            Color(1), Color(1), SceneVersionBinding.Published, SceneVersionBinding.Published));
        var childV1 = runtime.GetPublishedVersion(childId);

        runtime.SyncFrom(CreateState(
            childId, parentId, nestedLayerId, outputId,
            Color(1), Color(2), SceneVersionBinding.ExplicitVersion(childV1), SceneVersionBinding.Published));
        var parentV1 = runtime.GetPublishedVersion(parentId);

        for (var revision = 3; revision < 100; revision++)
        {
            runtime.SyncFrom(CreateState(
                childId, parentId, nestedLayerId, outputId,
                Color(revision), Color(revision + 100),
                SceneVersionBinding.Published,
                SceneVersionBinding.ExplicitVersion(parentV1)));
        }

        var pinned = runtime.CreateSnapshot().ProjectState.CanvasVersionSnapshots;
        Assert.Contains(parentV1, pinned.Keys);
        Assert.Contains(childV1, pinned.Keys);
        Assert.Equal(3, runtime.VersionRetentionSnapshot.DirectPinnedVersionCount);
        Assert.Equal(1, runtime.VersionRetentionSnapshot.TransitivePinnedVersionCount);
        Assert.Equal(4, runtime.VersionRetentionSnapshot.PinnedVersionCount);
        Assert.Equal(0, runtime.VersionRetentionSnapshot.ResolutionFailureCount);

        for (var revision = 100; revision < 200; revision++)
        {
            runtime.SyncFrom(CreateState(
                childId, parentId, nestedLayerId, outputId,
                Color(revision), Color(revision + 100),
                SceneVersionBinding.Published,
                SceneVersionBinding.Published));
        }

        var released = runtime.CreateSnapshot().ProjectState.CanvasVersionSnapshots;
        Assert.DoesNotContain(parentV1, released.Keys);
        Assert.DoesNotContain(childV1, released.Keys);
        Assert.Equal(2, runtime.VersionRetentionSnapshot.PinnedVersionCount);
        Assert.Equal(0, runtime.VersionRetentionSnapshot.TransitivePinnedVersionCount);
        Assert.True(runtime.VersionRetentionSnapshot.RetainedVersionCount <= 2 * SceneVersionStore.MaximumRetainedVersionsPerCanvas);
    }

    [Fact]
    public void Cyclic_explicit_version_graph_terminates_and_resolves_each_dependency_once()
    {
        var firstCanvas = CanvasId.New();
        var secondCanvas = CanvasId.New();
        var firstVersion = SceneVersionId.New();
        var secondVersion = SceneVersionId.New();
        var snapshots = new Dictionary<SceneVersionId, CanvasStateSnapshot>
        {
            [firstVersion] = CanvasWithExplicitNested(firstCanvas, secondCanvas, secondVersion),
            [secondVersion] = CanvasWithExplicitNested(secondCanvas, firstCanvas, firstVersion)
        };

        var resolution = SceneVersionDependencyResolver.Resolve([firstVersion], snapshots);

        Assert.Equal([secondVersion], resolution.Dependencies);
        Assert.Empty(resolution.Failures);
    }

    private static ProjectStateSnapshot CreateState(
        CanvasId childId,
        CanvasId parentId,
        DrawObjectId nestedLayerId,
        RenderOutputId outputId,
        ColorRgba childColor,
        ColorRgba parentColor,
        SceneVersionBinding nestedBinding,
        SceneVersionBinding outputBinding) =>
        new()
        {
            Version = Random.Shared.NextInt64(),
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = childId,
                    Name = "Child",
                    Size = new FrameSize(640, 360),
                    BackgroundColor = childColor
                },
                new CanvasStateSnapshot
                {
                    Id = parentId,
                    Name = "Parent",
                    Size = new FrameSize(1920, 1080),
                    BackgroundColor = parentColor,
                    Objects =
                    [
                        new CanvasDrawObjectSnapshot
                        {
                            Id = nestedLayerId,
                            Name = "Nested",
                            NestedCanvasId = childId,
                            VersionBinding = nestedBinding
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Output",
                    CanvasId = parentId,
                    OutputSize = new FrameSize(1920, 1080),
                    SceneVersionBinding = outputBinding,
                    RouteTransitionKind = OutputRouteTransitionKind.Cut
                }
            ]
        };

    private static CanvasStateSnapshot CanvasWithExplicitNested(
        CanvasId canvasId,
        CanvasId nestedCanvasId,
        SceneVersionId nestedVersionId) =>
        new()
        {
            Id = canvasId,
            Size = new FrameSize(1920, 1080),
            Objects =
            [
                new CanvasDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    NestedCanvasId = nestedCanvasId,
                    VersionBinding = SceneVersionBinding.ExplicitVersion(nestedVersionId)
                }
            ]
        };

    private static ColorRgba Color(int revision) =>
        ColorRgba.From(
            (revision % 251) / 250f,
            ((revision * 17) % 251) / 250f,
            ((revision * 43) % 251) / 250f,
            1f);
}
