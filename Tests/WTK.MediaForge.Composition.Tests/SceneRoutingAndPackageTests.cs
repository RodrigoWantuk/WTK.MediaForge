using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Project.Packages;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Geometry;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class SceneRoutingAndPackageTests
{
    [Fact]
    public void ProjectBuilder_routes_outputs_between_scenes()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Preview", 1280, 720, out var preview)
            .Scene("Program", 1920, 1080, out var program)
            .PreviewOutput("Panel A", preview, 1280, 720, out var output)
            .Route(program, output)
            .BuildValidated();

        var routedOutput = Assert.Single(project.Outputs);
        Assert.Equal(program.Id, routedOutput.CanvasId);
    }

    [Fact]
    public void Scene_package_exports_nested_canvases_sources_and_routes()
    {
        var project = CreateRoutedProject();
        var root = project.Canvases.Single(canvas => canvas.Name == "Program");

        var package = MediaForgeProjectPackages.ExportScene(project, root.Id);

        Assert.Equal(root.Id, package.RootCanvasId);
        Assert.Equal(2, package.Canvases.Count);
        Assert.Single(package.SourceDefinitions);
        Assert.Single(package.Outputs);
        Assert.Equal(root.Id, package.Outputs[0].CanvasId);
    }

    [Fact]
    public void Scene_package_export_redacts_stream_key_by_default()
    {
        var project = CreateRoutedProject();
        var root = project.Canvases.Single(canvas => canvas.Name == "Program");

        var package = MediaForgeProjectPackages.ExportScene(project, root.Id);
        var json = MediaForgePackageSerializer.SerializeScene(package);

        Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
        Assert.Contains("redacted", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_package_replace_import_returns_valid_project()
    {
        var project = CreateRoutedProject();
        var root = project.Canvases.Single(canvas => canvas.Name == "Program");
        var package = MediaForgeProjectPackages.ExportScene(
            project,
            root.Id,
            new MediaForgePackageExportOptions { IncludeSecrets = true });

        var result = MediaForgeProjectPackages.ImportScene(
            new MediaForgeProject(),
            package,
            MediaForgeProjectImportMode.ReplaceProject);

        Assert.True(result.Validation.IsValid);
        Assert.True(result.Applied);
        Assert.NotNull(result.Project);
        Assert.Equal(2, result.Project!.Canvases.Count);
    }

    [Fact]
    public void Scene_package_merge_as_new_scene_remaps_canvases_and_outputs_without_mutating_target()
    {
        var target = CreateRoutedProject();
        var root = target.Canvases.Single(canvas => canvas.Name == "Program");
        var package = MediaForgeProjectPackages.ExportScene(
            target,
            root.Id,
            new MediaForgePackageExportOptions { IncludeSecrets = true });

        var result = MediaForgeProjectPackages.ImportScene(
            target,
            package,
            MediaForgeProjectImportMode.MergeAsNewScene);

        Assert.Equal(2, target.Canvases.Count);
        Assert.True(result.Validation.IsValid);
        Assert.True(result.Applied);
        Assert.NotNull(result.Project);
        Assert.Equal(4, result.Project!.Canvases.Count);
        Assert.Equal(2, result.Project.Outputs.Count);
        Assert.Single(result.Project.SourceDefinitions);
        Assert.Contains(result.Project.Canvases, canvas => canvas.Name == "Program 2");
    }

    [Fact]
    public void Scene_package_dry_run_validates_without_applying()
    {
        var target = CreateRoutedProject();
        var root = target.Canvases.Single(canvas => canvas.Name == "Program");
        var package = MediaForgeProjectPackages.ExportScene(
            target,
            root.Id,
            new MediaForgePackageExportOptions { IncludeSecrets = true });

        var result = MediaForgeProjectPackages.ImportScene(
            target,
            package,
            MediaForgeProjectImportMode.DryRun);

        Assert.True(result.Validation.IsValid);
        Assert.False(result.Applied);
        Assert.Equal(2, target.Canvases.Count);
    }

    [Fact]
    public void Scene_package_merge_as_new_scene_remaps_conflicting_source_id_when_definition_differs()
    {
        var target = CreateRoutedProject();
        var root = target.Canvases.Single(canvas => canvas.Name == "Program");
        var package = MediaForgeProjectPackages.ExportScene(
            target,
            root.Id,
            new MediaForgePackageExportOptions { IncludeSecrets = true });

        var conflictingSourceId = target.SourceDefinitions.Single().Id;
        package.SourceDefinitions[0].Id = conflictingSourceId;
        package.SourceDefinitions[0].Settings = MediaSourceSettingsSerializer.ToJson(
            MediaForgeSources.Desktop(outputIndex: 1));

        var result = MediaForgeProjectPackages.ImportScene(
            target,
            package,
            MediaForgeProjectImportMode.MergeAsNewScene);

        Assert.True(result.Validation.IsValid);
        Assert.NotNull(result.Project);
        Assert.Equal(2, result.Project!.SourceDefinitions.Count);

        var importedCanvas = result.Project.Canvases.Single(canvas => canvas.Name == "Camera Box 2");
        var importedLayer = Assert.IsType<SourceLayerDrawObject>(Assert.Single(importedCanvas.Objects));
        Assert.NotEqual(conflictingSourceId, importedLayer.SourceId);
        Assert.Contains(result.Project.SourceDefinitions, source => source.Id == importedLayer.SourceId);
    }

    private static MediaForgeProject CreateRoutedProject()
    {
        return MediaForgeProjectBuilder.Create()
            .Scene("Camera Box", 640, 360, out var cameraBox)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop)
            .AddSourceLayer(
                cameraBox,
                desktop,
                layer => layer
                    .SetBounds(0, 0, 640, 360)
                    .SetStretch()
                    .AddChromaKey(ColorRgba.From(0, 1, 0, 1)))
            .Scene("Program", 1920, 1080, out var program)
            .AddCanvasLayer(
                program,
                cameraBox,
                layer => layer.SetBounds(100, 100, 640, 360))
            .RtmpOutput(
                "Main stream",
                program,
                "rtmp://example.test/live",
                "super-secret",
                1920,
                1080,
                out _)
            .BuildValidated();
    }
}
