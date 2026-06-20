using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Serialization;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class ProjectValidationTests
{
    [Fact]
    public void Valid_minimal_project_passes()
    {
        var project = CreateValidProject();
        var result = MediaForgeProjectValidator.Validate(project);
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Output_referencing_missing_canvas_fails()
    {
        var project = CreateValidProject();
        project.Outputs[0].CanvasId = CanvasId.New();

        var result = MediaForgeProjectValidator.Validate(project);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "output.canvas.missing");
    }

    [Fact]
    public void Source_layer_referencing_missing_source_fails()
    {
        var project = CreateValidProject();
        var layer = (SourceLayerDrawObject)project.Canvases[0].Objects[0];
        layer.SourceId = SourceId.New();

        var result = MediaForgeProjectValidator.Validate(project);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "drawobject.source.missing");
    }

    [Fact]
    public void Duplicate_canvas_id_fails()
    {
        var project = CreateValidProject();
        project.Canvases.Add(new MediaForgeCanvas
        {
            Id = project.Canvases[0].Id,
            Name = "Duplicate"
        });

        var result = MediaForgeProjectValidator.Validate(project);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "canvas.id.duplicate");
    }

    [Fact]
    public void Invalid_crop_fails()
    {
        var project = CreateValidProject();
        project.Canvases[0].Objects[0].Crop = new NormalizedRect(0.5f, 0, 0.25f, 1);

        var result = MediaForgeProjectValidator.Validate(project);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "drawobject.crop.invalid");
    }

    [Fact]
    public void Loader_rejects_invalid_project()
    {
        var project = CreateValidProject();
        project.Outputs[0].CanvasId = CanvasId.New();

        var loadResult = MediaForgeProjectLoader.Load(project);
        Assert.Null(loadResult.Project);
        Assert.False(loadResult.Validation.IsValid);
    }

    [Fact]
    public void Loader_accepts_valid_project()
    {
        var loadResult = MediaForgeProjectLoader.Load(CreateValidProject());
        Assert.NotNull(loadResult.Project);
        Assert.True(loadResult.Validation.IsValid);
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
                    Name = "Desktop 1",
                    TypeId = MediaSourceTypes.Desktop,
                    Settings = MediaSourceSettingsSerializer.ToJson(new DesktopCaptureSourceSettings
                    {
                        AdapterIndex = 0,
                        OutputIndex = 0
                    })
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
                            Id = DrawObjectId.New(),
                            Name = "Desktop Layer",
                            SourceId = sourceId,
                            Transform = new Transform2D
                            {
                                Position = new CanvasPoint(0, 0),
                                Size = new CanvasSize(1920, 1080)
                            }
                        },
                        new TextDrawObject
                        {
                            Id = DrawObjectId.New(),
                            Name = "Title",
                            Text = "Hello",
                            Transform = new Transform2D
                            {
                                Position = new CanvasPoint(16, 16),
                                Size = new CanvasSize(400, 64)
                            }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    Id = RenderOutputId.New(),
                    Name = "Preview",
                    CanvasId = canvasId,
                    OutputSize = new FrameSize(1920, 1080)
                }
            ]
        };
    }
}

public class ProjectJsonRoundTripTests
{
    [Fact]
    public void Round_trip_preserves_all_draw_object_types()
    {
        var sourceId = SourceId.New();
        var mainCanvasId = CanvasId.New();
        var nestedCanvasId = CanvasId.New();

        var original = new MediaForgeProject
        {
            SourceDefinitions =
            [
                new MediaForgeSourceDefinition
                {
                    Id = sourceId,
                    Name = "Image",
                    TypeId = MediaSourceTypes.ImageFile,
                    Settings = MediaSourceSettingsSerializer.ToJson(new ImageFileSourceSettings
                    {
                        Path = "C:\\test.png"
                    })
                }
            ],
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = nestedCanvasId,
                    Name = "Nested",
                    Size = new FrameSize(640, 480)
                },
                new MediaForgeCanvas
                {
                    Id = mainCanvasId,
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = sourceId,
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(800, 600),
                                Pivot = NormalizedPoint.Center
                            }
                        },
                        new TextDrawObject
                        {
                            Id = DrawObjectId.New(),
                            Name = "Text",
                            Text = "Overlay",
                            TextColor = ColorRgba.White
                        },
                        new SolidDrawObject
                        {
                            Id = DrawObjectId.New(),
                            Name = "Bar",
                            FillColor = ColorRgba.Black
                        },
                        new CanvasDrawObject
                        {
                            Id = DrawObjectId.New(),
                            Name = "PiP",
                            NestedCanvasId = nestedCanvasId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new MediaForgeRenderOutput
                {
                    Id = RenderOutputId.New(),
                    Name = "Out",
                    CanvasId = mainCanvasId,
                    OutputSize = new FrameSize(1280, 720),
                    CanvasLayoutMode = LayoutMode.Fit
                }
            ]
        };

        var json = MediaForgeProjectSerializer.Serialize(original);
        var restored = MediaForgeProjectSerializer.Deserialize(json);

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.SourceDefinitions.Count, restored.SourceDefinitions.Count);
        Assert.Equal(original.Canvases.Count, restored.Canvases.Count);
        Assert.Equal(4, restored.Canvases[1].Objects.Count);
        Assert.IsType<SourceLayerDrawObject>(restored.Canvases[1].Objects[0]);
        Assert.IsType<TextDrawObject>(restored.Canvases[1].Objects[1]);
        Assert.IsType<SolidDrawObject>(restored.Canvases[1].Objects[2]);
        Assert.IsType<CanvasDrawObject>(restored.Canvases[1].Objects[3]);

        var validation = MediaForgeProjectValidator.Validate(restored);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(i => i.Message)));

        var loadResult = MediaForgeProjectLoader.Load(restored);
        Assert.NotNull(loadResult.Project);
    }

    [Fact]
    public void Json_contains_stable_type_discriminators()
    {
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Objects =
                    [
                        new SourceLayerDrawObject { Transform = new Transform2D { Size = new CanvasSize(100, 100) } },
                        new TextDrawObject { Transform = new Transform2D { Size = new CanvasSize(100, 100) } }
                    ]
                }
            ]
        };

        var json = MediaForgeProjectSerializer.Serialize(project);
        Assert.Contains("\"$type\": \"source.layer\"", json);
        Assert.Contains("\"$type\": \"text\"", json);
    }
}
