using System.Collections.Immutable;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class ProjectLoaderSafetyTests
{
    [Fact]
    public void LoadFromJson_invalid_json_returns_null_project()
    {
        var result = MediaForgeProjectLoader.LoadFromJson("{ not json");

        Assert.Null(result.Project);
        Assert.False(result.Validation.IsValid);
        Assert.Contains(result.Validation.Issues, i => i.Code == "project.json.invalid");
    }

    [Fact]
    public void LoadFromJson_unknown_type_returns_null_project()
    {
        var project = CreateMinimalProjectForLoaderTests();
        var json = MediaForgeProjectSerializer.Serialize(project)
            .Replace("\"source.layer\"", "\"unknown.type\"");

        var result = MediaForgeProjectLoader.LoadFromJson(json);

        Assert.Null(result.Project);
        Assert.False(result.Validation.IsValid);
    }

    [Fact]
    public void LoadFromJson_empty_guid_is_rejected_by_validator()
    {
        var project = CreateMinimalProjectForLoaderTests();
        project.Canvases[0].Id = default;

        var result = MediaForgeProjectLoader.LoadFromJson(MediaForgeProjectSerializer.Serialize(project));

        Assert.Null(result.Project);
        Assert.False(result.Validation.IsValid);
        Assert.Contains(result.Validation.Issues, i => i.Code == "canvas.id.empty");
    }

    [Fact]
    public void LoadFromJson_zero_output_size_is_rejected_by_validator()
    {
        var canvasId = CanvasId.New();
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = canvasId,
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(0, 1080)
                }
            ]
        };

        var result = MediaForgeProjectLoader.LoadFromJson(MediaForgeProjectSerializer.Serialize(project));

        Assert.Null(result.Project);
        Assert.Contains(result.Validation.Issues, i => i.Code == "output.size.invalid");
    }

    private static MediaForgeProject CreateMinimalProjectForLoaderTests()
    {
        var sourceId = SourceId.New();
        var canvasId = CanvasId.New();

        return new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Id = sourceId,
                    Name = "Desktop",
                    TypeId = MediaSourceTypeId.DesktopCapture
                }
            ],
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = canvasId,
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };
    }
}

public class ProjectValidatorFiniteTests
{
    [Fact]
    public void NaN_opacity_fails_validation()
    {
        var project = CreateValidProject();
        project.Canvases[0].Objects[0].Opacity = float.NaN;

        var result = MediaForgeProjectValidator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "drawobject.opacity.invalid");
    }

    [Fact]
    public void NaN_pivot_fails_validation()
    {
        var project = CreateValidProject();
        project.Canvases[0].Objects[0].Transform = new Transform2D
        {
            Size = new CanvasSize(100, 100),
            Pivot = new NormalizedPoint(float.NaN, 0.5f)
        };

        var result = MediaForgeProjectValidator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "drawobject.transform.pivot");
    }

    [Fact]
    public void NaN_font_size_fails_validation()
    {
        var project = CreateValidProject();
        project.Canvases[0].Objects.Add(new TextDrawObject
        {
            Transform = new Transform2D { Size = new CanvasSize(100, 100) },
            FontSize = float.NaN
        });

        var result = MediaForgeProjectValidator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "drawobject.text.font");
    }

    private static MediaForgeProject CreateValidProject()
    {
        var sourceId = SourceId.New();
        var canvasId = CanvasId.New();

        return new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Id = sourceId,
                    Name = "Desktop",
                    TypeId = MediaSourceTypeId.DesktopCapture
                }
            ],
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = canvasId,
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };
    }
}

public class RenderFrameSnapshotFactorySafetyTests
{
    [Fact]
    public void Disabled_source_layer_does_not_acquire_frame()
    {
        var sourceId = SourceId.New();
        var source = new FakeVideoFrameSource(sourceId, "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        source.PublishFrame(1, MediaTime.Zero);

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Disabled",
                            SourceId = sourceId,
                            Enabled = false,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);

        Assert.Equal(0, source.RetainCount);
        Assert.Empty(result.Snapshot!.FrameLeases);
    }

    [Fact]
    public void Build_releases_leases_when_build_fails_after_acquire()
    {
        var goodSourceId = SourceId.New();
        var goodSource = new FakeVideoFrameSource(goodSourceId, "Good", new FrameSize(640, 480));
        goodSource.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        goodSource.PublishFrame(1, MediaTime.Zero);

        var badSourceId = SourceId.New();
        var badSource = new ThrowingAcquireVideoFrameSource(badSourceId, "Bad");

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(goodSource);
        runtime.RegisterFrameProvider(badSource);

        var canvasId = CanvasId.New();
        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = canvasId,
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Good",
                            SourceId = goodSourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        },
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Bad",
                            SourceId = badSourceId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) }
                        }
                    ]
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            RenderFrameSnapshotFactory.Build(projectState, runtime));

        Assert.Equal(0, goodSource.RetainCount);
    }

    [Fact]
    public void Source_no_frame_diagnostic_has_warning_severity()
    {
        var sourceId = SourceId.New();
        var source = new FakeVideoFrameSource(sourceId, "Fake", new FrameSize(640, 480));
        source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(source);

        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(SnapshotDiagnosticKind.SourceNoFrame, diagnostic.Kind);
        Assert.Equal(SnapshotDiagnosticSeverity.Warning, diagnostic.Severity);
    }
}

internal sealed class ThrowingAcquireVideoFrameSource : IVideoFrameProvider
{
    public ThrowingAcquireVideoFrameSource(SourceId id, string name)
    {
        Id = id;
        Name = name;
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State { get; private set; } = MediaSourceState.Running;

    public Exception? LastError { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public bool TryAcquireLatestFrame(out GpuFrameLease lease) =>
        throw new InvalidOperationException("Simulated acquire failure.");
}
