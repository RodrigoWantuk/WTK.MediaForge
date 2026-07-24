using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class CanvasGraphValidationTests
{
    [Fact]
    public void Self_referencing_canvas_fails_validation()
    {
        var canvasId = CanvasId.New();
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = canvasId,
                    Name = "Loop",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new CanvasDrawObject
                        {
                            Name = "Self",
                            NestedCanvasId = canvasId,
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                        }
                    ]
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "canvas.nested.self");
    }

    [Fact]
    public void Two_canvas_cycle_fails_validation()
    {
        var canvasA = CanvasId.New();
        var canvasB = CanvasId.New();

        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Id = canvasA,
                    Name = "A",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new CanvasDrawObject
                        {
                            NestedCanvasId = canvasB,
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                        }
                    ]
                },
                new MediaForgeCanvas
                {
                    Id = canvasB,
                    Name = "B",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new CanvasDrawObject
                        {
                            NestedCanvasId = canvasA,
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                        }
                    ]
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "canvas.nested.cycle");
    }

    [Fact]
    public void Depth_within_limit_passes_validation()
    {
        var project = BuildLinearNestedChain(CanvasGraphLimits.MaxNestedCanvasDepth);
        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(i => i.Message)));
    }

    [Fact]
    public void Depth_exceeding_limit_fails_validation()
    {
        var project = BuildLinearNestedChain(CanvasGraphLimits.MaxNestedCanvasDepth + 1);
        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "canvas.nested.depth");
    }

    private static MediaForgeProject BuildLinearNestedChain(int depth)
    {
        var canvases = new List<MediaForgeCanvas>();
        var ids = Enumerable.Range(0, depth + 1).Select(_ => CanvasId.New()).ToArray();

        for (var i = 0; i < ids.Length; i++)
        {
            MediaForgeDrawObject[] objects = i == ids.Length - 1
                ? []
                :
                [
                    new CanvasDrawObject
                    {
                        Name = $"Nested {i}",
                        NestedCanvasId = ids[i + 1],
                        Transform = new Transform2D { Size = new CanvasSize(100, 100) }
                    }
                ];

            canvases.Add(new MediaForgeCanvas
            {
                Id = ids[i],
                Name = $"Canvas {i}",
                Size = new FrameSize(1920, 1080),
                Objects = objects.ToList()
            });
        }

        return new MediaForgeProject { Canvases = canvases };
    }
}
