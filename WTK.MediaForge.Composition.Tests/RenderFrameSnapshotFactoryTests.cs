using System.Collections.Immutable;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderFrameSnapshotFactoryTests
{
    [Fact]
    public void RenderFrameSnapshot_preserves_effects_on_source_layer()
    {
        var sourceId = SourceId.New();
        var effectId = EffectId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(1, MediaTime.Zero);

        var projectState = CreateProjectStateWithEffects(
            sourceId,
            new ChromaKeyEffectSnapshot { Id = effectId, Name = "Key", Similarity = 0.35f },
            drawObjectFactory: (effects, sid) => new SourceLayerDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Layer",
                SourceId = sid,
                Transform = new Transform2D { Size = new CanvasSize(640, 480) },
                Effects = effects
            });

        AssertEffectsPreserved<RenderSourceLayerDrawObjectSnapshot, ChromaKeyEffectSnapshot>(
            projectState, source, effectId, e => Assert.Equal(0.35f, e.Similarity));
    }

    [Fact]
    public void RenderFrameSnapshot_preserves_effects_on_text()
    {
        var effectId = EffectId.New();
        var projectState = CreateProjectStateWithEffects(
            SourceId.New(),
            new ColorCorrectionEffectSnapshot { Id = effectId, Name = "Grade", Brightness = 0.1f },
            drawObjectFactory: (effects, _) => new TextDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Title",
                Transform = new Transform2D { Size = new CanvasSize(200, 64) },
                Effects = effects
            },
            requiresSource: false);

        AssertEffectsPreserved<RenderTextDrawObjectSnapshot, ColorCorrectionEffectSnapshot>(
            projectState, source: null, effectId, e => Assert.Equal(0.1f, e.Brightness));
    }

    [Fact]
    public void RenderFrameSnapshot_preserves_effects_on_solid()
    {
        var effectId = EffectId.New();
        var projectState = CreateProjectStateWithEffects(
            SourceId.New(),
            new BlurEffectSnapshot { Id = effectId, Name = "Soft", Radius = 8f },
            drawObjectFactory: (effects, _) => new SolidDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Bar",
                Transform = new Transform2D { Size = new CanvasSize(100, 100) },
                Effects = effects
            },
            requiresSource: false);

        AssertEffectsPreserved<RenderSolidDrawObjectSnapshot, BlurEffectSnapshot>(
            projectState, source: null, effectId, e => Assert.Equal(8f, e.Radius));
    }

    [Fact]
    public void RenderFrameSnapshot_preserves_effects_on_canvas_layer()
    {
        var sourceId = SourceId.New();
        var effectId = EffectId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(1, MediaTime.Zero);

        var nestedCanvasId = CanvasId.New();
        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new CanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "PiP",
                            NestedCanvasId = nestedCanvasId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) },
                            Effects = [new TransitionEffectSnapshot { Id = effectId, Name = "Fade", Progress = 0.5f }]
                        }
                    ]
                },
                new CanvasStateSnapshot
                {
                    Id = nestedCanvasId,
                    Name = "Nested",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Nested Layer",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var pip = Assert.IsType<RenderCanvasDrawObjectSnapshot>(result.TakeSnapshot()!.Canvases[0].Objects[0]);
        var effect = Assert.IsType<TransitionEffectSnapshot>(Assert.Single(pip.Effects));
        Assert.Equal(effectId, effect.Id);
        Assert.Equal(0.5f, effect.Progress);
    }

    [Fact]
    public void Disabled_canvas_layer_preserves_effects()
    {
        var effectId = EffectId.New();
        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new CanvasDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Disabled PiP",
                            Enabled = false,
                            NestedCanvasId = CanvasId.New(),
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) },
                            Effects = [new BlurEffectSnapshot { Id = effectId, Name = "Soft", Radius = 4f }]
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, new CompositionRuntime());
        var pip = Assert.IsType<RenderCanvasDrawObjectSnapshot>(result.TakeSnapshot()!.Canvases[0].Objects[0]);
        Assert.False(pip.Enabled);
        Assert.Null(pip.NestedCanvas);
        var effect = Assert.IsType<BlurEffectSnapshot>(Assert.Single(pip.Effects));
        Assert.Equal(effectId, effect.Id);
    }

    private static void AssertEffectsPreserved<TRender, TEffect>(
        ProjectStateSnapshot projectState,
        FakeVideoFrameSource? source,
        EffectId effectId,
        Action<TEffect> assertEffect)
        where TRender : RenderDrawObjectSnapshot
        where TEffect : EffectStateSnapshot
    {
        var runtime = new CompositionRuntime();
        if (source is not null)
            runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var renderObject = Assert.IsType<TRender>(result.TakeSnapshot()!.Canvases[0].Objects[0]);
        var effect = Assert.IsType<TEffect>(Assert.Single(renderObject.Effects));
        Assert.Equal(effectId, effect.Id);
        assertEffect(effect);
    }

    private static ProjectStateSnapshot CreateProjectStateWithEffects(
        SourceId sourceId,
        EffectStateSnapshot effect,
        Func<ImmutableArray<EffectStateSnapshot>, SourceId, DrawObjectStateSnapshot> drawObjectFactory,
        bool requiresSource = true)
    {
        return new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects = [drawObjectFactory([effect], sourceId)]
                }
            ]
        };
    }

    [Fact]
    public void Build_binds_source_frames_with_effective_crop()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(42, new MediaTime(16_000_000));

        var projectState = CreateProjectState(sourceId, includeNested: false);
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();
        Assert.NotNull(snapshot);

        var layer = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot!.Canvases[0].Objects[0]);
        Assert.NotNull(layer.BoundFrame);
        Assert.Equal(42, layer.BoundFrame!.Value.FrameNumber);
        Assert.Equal(NormalizedRect.Full, layer.EffectiveCrop);

        snapshot.Dispose();
    }

    [Fact]
    public void Build_deduplicates_leases_for_same_source()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(10, MediaTime.Zero);

        var mainCanvasId = CanvasId.New();
        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = mainCanvasId,
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer A",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        },
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer B",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) }
                        }
                    ]
                }
            ]
        };

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();
        Assert.NotNull(snapshot);

        Assert.Single(snapshot!.FrameLeases);
        Assert.Equal(1, source.RetainCount);

        var layerA = (RenderSourceLayerDrawObjectSnapshot)snapshot.Canvases[0].Objects[0];
        var layerB = (RenderSourceLayerDrawObjectSnapshot)snapshot.Canvases[0].Objects[1];
        Assert.Equal(layerA.BoundFrame!.Value.FrameNumber, layerB.BoundFrame!.Value.FrameNumber);

        snapshot.Dispose();
        Assert.Equal(1, source.RetainCount);
        runtime.Dispose();
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Build_emits_diagnostic_when_no_frame_available()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);

        var projectState = CreateProjectState(sourceId, includeNested: false);
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();
        Assert.NotNull(snapshot);

        var layer = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot!.Canvases[0].Objects[0]);
        Assert.Null(layer.BoundFrame);
        Assert.Contains(result.Diagnostics, d => d.Kind == SnapshotDiagnosticKind.SourceNoFrame);

        snapshot.Dispose();
    }

    [Fact]
    public void TakeSnapshot_transfers_ownership_from_build_result()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var result = RenderFrameSnapshotFactory.Build(CreateProjectState(sourceId, includeNested: false), runtime);
        var snapshot = result.TakeSnapshot();

        Assert.NotNull(snapshot);
        Assert.Null(result.Snapshot);

        result.Dispose();
        Assert.Equal(1, source.RetainCount);

        snapshot!.Dispose();
        Assert.Equal(1, source.RetainCount);
        runtime.Dispose();
        Assert.Equal(0, source.RetainCount);
    }

    [Fact]
    public void Build_resolves_nested_canvas_one_level()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(5, MediaTime.Zero);

        var projectState = CreateProjectState(sourceId, includeNested: true);
        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();
        Assert.NotNull(snapshot);

        var pip = Assert.IsType<RenderCanvasDrawObjectSnapshot>(snapshot!.Canvases[0].Objects[1]);
        Assert.NotNull(pip.NestedCanvas);
        Assert.Single(pip.NestedCanvas!.Objects);
        Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(pip.NestedCanvas.Objects[0]);

        snapshot.Dispose();
    }

    [Fact]
    public void Build_applies_custom_crop_to_effective_crop()
    {
        var sourceId = SourceId.New();
        var source = CreateRunningSource(sourceId);
        source.PublishFrame(1, MediaTime.Zero);

        var crop = new NormalizedRect(0.1f, 0.2f, 0.9f, 0.8f);
        var projectState = CreateProjectState(sourceId, includeNested: false, crop: crop);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();

        var layer = Assert.IsType<RenderSourceLayerDrawObjectSnapshot>(snapshot!.Canvases[0].Objects[0]);
        Assert.Equal(crop, layer.EffectiveCrop);

        snapshot!.Dispose();
    }

    private static FakeVideoFrameSource CreateRunningSource(SourceId sourceId)
    {
        var source = new FakeVideoFrameSource(sourceId, "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return source;
    }

    private static ProjectStateSnapshot CreateProjectState(
        SourceId sourceId,
        bool includeNested,
        NormalizedRect? crop = null)
    {
        var mainCanvasId = CanvasId.New();
        var nestedCanvasId = CanvasId.New();

        var nestedCanvas = new CanvasStateSnapshot
        {
            Id = nestedCanvasId,
            Name = "Nested",
            Size = new FrameSize(640, 480),
            Objects =
            [
                new SourceLayerDrawObjectSnapshot
                {
                    Id = DrawObjectId.New(),
                    Name = "Nested Layer",
                    SourceId = sourceId,
                    Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                }
            ]
        };

        var mainObjects = new List<DrawObjectStateSnapshot>
        {
            new SourceLayerDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "Main Layer",
                SourceId = sourceId,
                Crop = crop,
                Transform = new Transform2D { Size = new CanvasSize(1920, 1080) }
            }
        };

        if (includeNested)
        {
            mainObjects.Add(new CanvasDrawObjectSnapshot
            {
                Id = DrawObjectId.New(),
                Name = "PiP",
                NestedCanvasId = nestedCanvasId,
                Transform = new Transform2D { Size = new CanvasSize(320, 240) }
            });
        }

        var canvases = new List<CanvasStateSnapshot>
        {
            new()
            {
                Id = mainCanvasId,
                Name = "Main",
                Size = new FrameSize(1920, 1080),
                Objects = mainObjects.ToImmutableArray()
            }
        };

        if (includeNested)
            canvases.Add(nestedCanvas);

        return new ProjectStateSnapshot
        {
            Version = 1,
            Canvases = canvases.ToImmutableArray()
        };
    }
}
