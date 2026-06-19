using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class ProjectStateSnapshotTests
{
    [Fact]
    public void Snapshot_does_not_share_mutable_references_with_project()
    {
        var project = CreateSampleProject();
        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);

        project.SourceDefinitions[0].Name = "Changed";
        project.SourceDefinitions[0].Settings["adapterIndex"] = 99;
        project.Canvases[0].Name = "Changed Canvas";
        project.Canvases[0].Size = new FrameSize(640, 480);
        project.Canvases[0].Objects[0].Name = "Changed Layer";
        project.Canvases[0].Objects[0].Transform = new Transform2D
        {
            Position = new CanvasPoint(100, 100),
            Size = new CanvasSize(100, 100)
        };
        project.Outputs[0].Name = "Changed Output";

        Assert.Equal("Desktop 1", snapshot.Sources[0].Name);
        Assert.Equal(0, snapshot.Sources[0].Settings["adapterIndex"]!.GetValue<int>());
        Assert.Equal("Main", snapshot.Canvases[0].Name);
        Assert.Equal(1920u, snapshot.Canvases[0].Size.Width);
        Assert.Equal("Desktop Layer", snapshot.Canvases[0].Objects[0].Name);
        Assert.Equal(0f, snapshot.Canvases[0].Objects[0].Transform.Position.X);
        Assert.Equal("Preview", snapshot.Outputs[0].Name);
    }

    [Fact]
    public void Snapshot_settings_is_deep_cloned()
    {
        var project = CreateSampleProject();
        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);

        Assert.NotSame(project.SourceDefinitions[0].Settings, snapshot.Sources[0].Settings);

        project.SourceDefinitions[0].Settings["adapterIndex"] = 42;
        Assert.Equal(0, snapshot.Sources[0].Settings["adapterIndex"]!.GetValue<int>());
    }

    [Fact]
    public void Snapshot_version_increments()
    {
        var project = CreateSampleProject();

        var first = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        var second = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);

        Assert.True(first.Version >= 1);
        Assert.True(second.Version > first.Version);
    }

    [Fact]
    public void Snapshot_preserves_draw_object_types()
    {
        var project = CreateSampleProject();
        project.Canvases[0].Objects.Add(new TextDrawObject
        {
            Id = DrawObjectId.New(),
            Name = "Title",
            Transform = new Transform2D { Size = new CanvasSize(200, 40) }
        });

        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);

        Assert.IsType<SourceLayerDrawObjectSnapshot>(snapshot.Canvases[0].Objects[0]);
        Assert.IsType<TextDrawObjectSnapshot>(snapshot.Canvases[0].Objects[1]);
    }

    private static MediaForgeProject CreateSampleProject()
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
                    Settings = new JsonObject { ["adapterIndex"] = 0, ["outputIndex"] = 0 }
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
                                Size = new CanvasSize(1920, 1080)
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
