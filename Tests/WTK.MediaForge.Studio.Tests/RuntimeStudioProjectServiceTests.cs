using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class RuntimeStudioProjectServiceTests
{
    [Fact]
    public async Task Save_and_open_roundtrip_uses_canonical_engine_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "project.mforge.json");
        try
        {
            var mapper = new StudioProjectEngineMapper();
            var service = new RuntimeStudioProjectService(mapper, path);
            var original = StudioMockDocumentFactory.Create();

            await service.SaveAsync(original, path, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            var canonical = MediaForgeProjectSerializer.Deserialize(json);
            var restored = await service.OpenAsync(path, CancellationToken.None);

            Assert.Equal(original.Scenes.Count, canonical.Canvases.Count);
            Assert.Equal(original.Sources.Count(source => source.TypeId is not ("source.text" or "source.solid")), canonical.SourceDefinitions.Count);
            Assert.Equal(original.Scenes.Count, restored.Scenes.Count);
            Assert.Equal(original.Outputs.Count, restored.Outputs.Count);
            Assert.False(restored.HasUnsavedChanges);
            Assert.Equal(Path.GetFullPath(path), service.Current.Path);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Open_rejects_invalid_project_without_changing_current_document()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "invalid.mforge.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":999}");
        try
        {
            var service = new RuntimeStudioProjectService(new StudioProjectEngineMapper(), path);
            var previousName = service.Current.DisplayName;

            await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAsync(path, CancellationToken.None));

            Assert.Equal(previousName, service.Current.DisplayName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Open_edit_save_preserves_canonical_fields_not_exposed_by_studio()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "project.mforge.json");
        Directory.CreateDirectory(directory);
        try
        {
            var mapper = new StudioProjectEngineMapper();
            var project = mapper.CreateProject(StudioMockDocumentFactory.Create());
            var source = project.SourceDefinitions.First();
            source.Settings["futureProviderSetting"] = new System.Text.Json.Nodes.JsonObject
            {
                ["mode"] = "vendor-specific"
            };
            var output = project.Outputs.First(candidate => candidate.TypeId == RenderOutputTypes.RecordingMp4);
            output.Enabled = false;
            output.OutputSize = new FrameSize(1280, 720);
            output.LetterboxColor = ColorRgba.From(0.1f, 0.2f, 0.3f, 0.4f);
            output.SceneVersionBinding = SceneVersionBinding.ExplicitVersion(SceneVersionId.New());
            output.Settings["futureMuxSetting"] = 73;
            output.Settings["video"]!["bitrateBitsPerSecond"] = 12_345_678;
            var canvas = project.Canvases.First();
            canvas.BackgroundColor = ColorRgba.From(0.2f, 0.3f, 0.4f, 0.5f);
            var nestedBinding = SceneVersionBinding.ExplicitVersion(SceneVersionId.New());
            var nestedLayerId = DrawObjectId.New();
            canvas.Objects.Add(new CanvasDrawObject
            {
                Id = nestedLayerId,
                Name = "Cena aninhada preservada",
                NestedCanvasId = project.Canvases[1].Id,
                VersionBinding = nestedBinding,
                Transform = new Transform2D { Size = new CanvasSize(640, 360) }
            });
            var layer = canvas.Objects.First();
            var textLayer = project.Canvases
                .SelectMany(static item => item.Objects)
                .OfType<TextDrawObject>()
                .First();
            textLayer.FontFamily = "Studio preserved font";
            textLayer.FontSize = 47f;
            textLayer.TextColor = ColorRgba.From(0.7f, 0.6f, 0.5f, 0.4f);
            var colorEffectId = EffectId.New();
            layer.Effects.Add(new ColorCorrectionEffect
            {
                Id = colorEffectId,
                Name = "Correção preservada",
                Brightness = 0.17f,
                Contrast = 1.23f,
                Saturation = 0.81f,
                HueDegrees = 19f
            });
            await File.WriteAllTextAsync(path, MediaForgeProjectSerializer.Serialize(project));

            var service = new RuntimeStudioProjectService(mapper, path);
            var document = await service.OpenAsync(path, CancellationToken.None);
            document.Scenes.First().Layers.First().Transform.Opacity = 61;
            document.Scenes
                .SelectMany(static scene => scene.Layers)
                .Single(candidate => candidate.Id == textLayer.Id.Value.ToString("D"))
                .SourceName = "Texto editado";
            document.Outputs.Single(candidate => candidate.Id == output.Id.Value.ToString("D")).DisplayName = "Arquivo principal";
            await service.SaveAsync(document, path, CancellationToken.None);

            var restored = MediaForgeProjectSerializer.Deserialize(await File.ReadAllTextAsync(path));
            var restoredSource = restored.SourceDefinitions.Single(candidate => candidate.Id == source.Id);
            var restoredOutput = restored.Outputs.Single(candidate => candidate.Id == output.Id);
            var restoredCanvas = restored.Canvases.Single(candidate => candidate.Id == canvas.Id);
            var restoredLayer = restoredCanvas.Objects.Single(candidate => candidate.Id == layer.Id);
            var restoredNested = Assert.IsType<CanvasDrawObject>(
                restoredCanvas.Objects.Single(candidate => candidate.Id == nestedLayerId));
            var restoredColor = Assert.IsType<ColorCorrectionEffect>(
                restoredLayer.Effects.Single(candidate => candidate.Id == colorEffectId));
            var restoredText = restored.Canvases
                .SelectMany(static item => item.Objects)
                .OfType<TextDrawObject>()
                .Single(candidate => candidate.Id == textLayer.Id);

            Assert.Equal("vendor-specific", restoredSource.Settings["futureProviderSetting"]!["mode"]!.GetValue<string>());
            Assert.False(restoredOutput.Enabled);
            Assert.Equal("Arquivo principal", restoredOutput.Name);
            Assert.Equal(new FrameSize(1280, 720), restoredOutput.OutputSize);
            Assert.Equal(73, restoredOutput.Settings["futureMuxSetting"]!.GetValue<int>());
            Assert.Equal(12_345_678, restoredOutput.Settings["video"]!["bitrateBitsPerSecond"]!.GetValue<int>());
            Assert.Equal(0.4f, restoredOutput.LetterboxColor.A, precision: 3);
            Assert.Equal(output.SceneVersionBinding, restoredOutput.SceneVersionBinding);
            Assert.Equal(0.5f, restoredCanvas.BackgroundColor.A, precision: 3);
            Assert.Equal(0.61f, restoredLayer.Opacity, precision: 3);
            Assert.Equal(0.17f, restoredColor.Brightness, precision: 3);
            Assert.Equal(1.23f, restoredColor.Contrast, precision: 3);
            Assert.Equal(0.81f, restoredColor.Saturation, precision: 3);
            Assert.Equal(19f, restoredColor.HueDegrees, precision: 3);
            Assert.Equal(nestedBinding, restoredNested.VersionBinding);
            Assert.Equal("Texto editado", restoredText.Text);
            Assert.Equal("Studio preserved font", restoredText.FontFamily);
            Assert.Equal(47f, restoredText.FontSize);
            Assert.Equal(0.4f, restoredText.TextColor.A, precision: 3);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cancelled_save_preserves_existing_file_and_removes_temporary_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtk-mediaforge-studio-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "project.mforge.json");
        Directory.CreateDirectory(directory);
        try
        {
            var mapper = new StudioProjectEngineMapper();
            var originalProject = mapper.CreateProject(StudioMockDocumentFactory.Create());
            var originalJson = MediaForgeProjectSerializer.Serialize(originalProject);
            await File.WriteAllTextAsync(path, originalJson);
            var service = new RuntimeStudioProjectService(mapper, path);
            var document = await service.OpenAsync(path, CancellationToken.None);
            document.Scenes.First().DisplayName = "Alteração cancelada";
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.SaveAsync(document, path, cancellation.Token));

            Assert.Equal(originalJson, await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
