using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioProjectSessionRoundTripTests
{
    [Fact]
    public void No_edit_round_trip_is_semantically_lossless_for_editable_read_only_and_advanced_state()
    {
        var mapper = new StudioProjectEngineMapper();
        var project = mapper.CreateProject(StudioMockDocumentFactory.Create());
        var canvas = project.Canvases[0];
        var nestedCanvas = project.Canvases[1];

        var windowSource = new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = "Window capture read-only",
            TypeId = MediaSourceTypes.WindowCapture,
            Settings = MediaSourceSettingsSerializer.ToJson(new WindowCaptureSourceSettings { WindowHandle = 12345 })
        };
        windowSource.Settings["futureWindowProperty"] = "preserve-me";
        project.SourceDefinitions.Add(windowSource);
        project.SourceDefinitions.Add(new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = "Generated future source",
            TypeId = MediaSourceTypes.Generated,
            Settings = MediaSourceSettingsSerializer.ToJson(new GeneratedSourceSettings { GeneratorKind = "vendor.future" })
        });

        canvas.Objects.Add(new SourceLayerDrawObject
        {
            Name = "Window",
            SourceId = windowSource.Id,
            Transform = new Transform2D { Size = new CanvasSize(800, 450) },
            LetterboxColor = ColorRgba.From(0.11f, 0.22f, 0.33f, 0.44f),
            ContentRotationOverride = DisplayRotation.Rotate90
        });
        var effectsLayer = canvas.Objects[0];
        effectsLayer.Effects =
        [
            new BlurEffect { Name = "Blur", Radius = 13.75f, Order = 0 },
            new ChromaKeyEffect
            {
                Name = "Chroma",
                KeyColor = ColorRgba.From(0.12f, 0.91f, 0.27f, 0.83f),
                Similarity = 0.47f,
                Smoothness = 0.19f,
                SpillReduction = 0.38f,
                Order = 1
            },
            new ColorCorrectionEffect
            {
                Name = "Color",
                Brightness = -0.17f,
                Contrast = 1.31f,
                Saturation = 0.76f,
                HueDegrees = 23.5f,
                Order = 2
            }
        ];
        canvas.Objects.Add(new SolidDrawObject
        {
            Name = "Alpha solid",
            FillColor = ColorRgba.From(0.2f, 0.4f, 0.6f, 0.35f),
            Transform = new Transform2D { Size = new CanvasSize(320, 180) }
        });
        canvas.Objects.Add(new CanvasDrawObject
        {
            Name = "Versioned nested",
            NestedCanvasId = nestedCanvas.Id,
            VersionBinding = SceneVersionBinding.ExplicitVersion(SceneVersionId.New()),
            Transform = new Transform2D { Size = new CanvasSize(640, 360) }
        });

        var text = project.Canvases.SelectMany(static item => item.Objects).OfType<TextDrawObject>().First();
        text.FontFamily = "Noto Sans Display";
        text.FontSize = 41.5f;
        text.TextColor = ColorRgba.From(0.71f, 0.52f, 0.33f, 0.42f);

        var offscreenSettings = MediaForgeOutputs.Offscreen();
        var offscreenOutput = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Read-only offscreen",
            Enabled = false,
            TypeId = offscreenSettings.TypeId,
            SchemaVersion = offscreenSettings.SchemaVersion,
            Settings = RenderOutputSettingsSerializer.ToJson(offscreenSettings),
            CanvasId = canvas.Id,
            OutputSize = new FrameSize(1024, 576),
            RouteTransition = OutputRouteTransition.Cut("offscreen-cut", "Offscreen cut")
        };
        project.Outputs.Add(offscreenOutput);

        foreach (var output in project.Outputs)
        {
            output.Enabled = false;
            output.SceneVersionBinding = SceneVersionBinding.ExplicitVersion(SceneVersionId.New());
            output.Settings["futureOutputProperty"] = new System.Text.Json.Nodes.JsonObject { ["value"] = 73 };
        }

        foreach (var source in project.SourceDefinitions)
            source.Settings["futureSourceProperty"] = source.TypeId.Value;

        var originalJson = MediaForgeProjectSerializer.Serialize(project);
        var session = StudioProjectSession.Open(mapper, project);

        Assert.Contains(session.Document.Sources, source =>
            source.Id == windowSource.Id.Value.ToString("D") &&
            source.ProjectionKind == StudioProjectionKind.KnownReadOnly);
        Assert.Contains(session.Document.Outputs, output =>
            output.Id == offscreenOutput.Id.Value.ToString("D") &&
            output.ProjectionKind == StudioProjectionKind.KnownReadOnly);

        var saved = session.CreateValidatedSaveSnapshot(session.Document);
        var savedJson = MediaForgeProjectSerializer.Serialize(saved);

        Assert.Equal(originalJson, savedJson);
    }
}
