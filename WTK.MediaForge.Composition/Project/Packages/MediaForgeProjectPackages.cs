using System.Text.Json;
using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Serialization;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Project.Packages;

public static class MediaForgeProjectPackages
{
    public static MediaForgeScenePackage ExportScene(
        MediaForgeProject project,
        CanvasId rootCanvasId,
        MediaForgePackageExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = project.Canvases.FirstOrDefault(canvas => canvas.Id == rootCanvasId)
            ?? throw new ArgumentException($"Canvas {rootCanvasId} was not found.", nameof(rootCanvasId));

        var canvasIds = CollectCanvasClosure(project, rootCanvasId);
        var canvases = project.Canvases
            .Where(canvas => canvasIds.Contains(canvas.Id))
            .Select(Clone)
            .ToList();

        var sourceIds = canvases
            .SelectMany(canvas => canvas.Objects)
            .OfType<SourceLayerDrawObject>()
            .Select(layer => layer.SourceId)
            .ToHashSet();

        var sources = project.SourceDefinitions
            .Where(source => sourceIds.Contains(source.Id))
            .Select(Clone)
            .ToList();

        var outputs = project.Outputs
            .Where(output => canvasIds.Contains(output.CanvasId))
            .Select(output => CloneOutput(output, options ?? new MediaForgePackageExportOptions()))
            .ToList();

        return new MediaForgeScenePackage
        {
            Name = root.Name,
            RootCanvasId = root.Id,
            SourceDefinitions = sources,
            Canvases = canvases,
            Outputs = outputs
        };
    }

    public static MediaForgeProjectImportResult ImportScene(
        MediaForgeProject targetProject,
        MediaForgeScenePackage package,
        MediaForgeProjectImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(targetProject);
        ArgumentNullException.ThrowIfNull(package);

        if (mode == MediaForgeProjectImportMode.MergePresetsOnly)
        {
            var clone = MediaForgeProjectCloner.DeepClone(targetProject);
            var validation = MediaForgeProjectValidator.Validate(clone);
            return new MediaForgeProjectImportResult
            {
                Project = validation.IsValid ? clone : null,
                Validation = validation,
                Applied = false
            };
        }

        var candidate = mode == MediaForgeProjectImportMode.ReplaceProject
            ? CreateProjectFromPackage(package)
            : MergePackageAsNewScene(targetProject, package);

        var validationResult = MediaForgeProjectLoader.Load(candidate);
        return new MediaForgeProjectImportResult
        {
            Project = validationResult.Project,
            Validation = validationResult.Validation,
            Applied = mode != MediaForgeProjectImportMode.DryRun && validationResult.Validation.IsValid
        };
    }

    private static MediaForgeProject CreateProjectFromPackage(MediaForgeScenePackage package) =>
        new()
        {
            SourceDefinitions = package.SourceDefinitions.Select(Clone).ToList(),
            Canvases = package.Canvases.Select(Clone).ToList(),
            Outputs = package.Outputs.Select(Clone).ToList()
        };

    private static MediaForgeProject MergePackageAsNewScene(
        MediaForgeProject targetProject,
        MediaForgeScenePackage package)
    {
        var merged = MediaForgeProjectCloner.DeepClone(targetProject);

        var sourceMap = new Dictionary<SourceId, SourceId>();
        foreach (var source in package.SourceDefinitions)
        {
            var existingSource = merged.SourceDefinitions.FirstOrDefault(existing => existing.Id == source.Id);
            if (existingSource is not null && AreEquivalentSourceDefinitions(existingSource, source))
            {
                sourceMap[source.Id] = source.Id;
                continue;
            }

            var clonedSource = Clone(source);
            if (existingSource is not null)
            {
                clonedSource.Id = SourceId.New();
                clonedSource.Name = CreateUniqueName(
                    merged.SourceDefinitions.Select(existing => existing.Name),
                    clonedSource.Name);
            }

            merged.SourceDefinitions.Add(clonedSource);
            sourceMap[source.Id] = clonedSource.Id;
        }

        var canvasMap = package.Canvases.ToDictionary(canvas => canvas.Id, _ => CanvasId.New());
        foreach (var canvas in package.Canvases)
        {
            var clonedCanvas = Clone(canvas);
            clonedCanvas.Id = canvasMap[canvas.Id];
            clonedCanvas.Name = CreateUniqueName(merged.Canvases.Select(existing => existing.Name), clonedCanvas.Name);

            foreach (var drawObject in clonedCanvas.Objects)
            {
                drawObject.Id = DrawObjectId.New();

                if (drawObject is SourceLayerDrawObject sourceLayer &&
                    sourceMap.TryGetValue(sourceLayer.SourceId, out var mappedSourceId))
                {
                    sourceLayer.SourceId = mappedSourceId;
                }
                else if (drawObject is CanvasDrawObject canvasLayer &&
                    canvasMap.TryGetValue(canvasLayer.NestedCanvasId, out var mappedCanvasId))
                {
                    canvasLayer.NestedCanvasId = mappedCanvasId;
                }
            }

            merged.Canvases.Add(clonedCanvas);
        }

        foreach (var output in package.Outputs)
        {
            if (!canvasMap.TryGetValue(output.CanvasId, out var mappedCanvasId))
                continue;

            var clonedOutput = Clone(output);
            clonedOutput.Id = RenderOutputId.New();
            clonedOutput.CanvasId = mappedCanvasId;
            clonedOutput.Name = CreateUniqueName(merged.Outputs.Select(existing => existing.Name), clonedOutput.Name);
            merged.Outputs.Add(clonedOutput);
        }

        return merged;
    }

    private static HashSet<CanvasId> CollectCanvasClosure(MediaForgeProject project, CanvasId rootCanvasId)
    {
        var result = new HashSet<CanvasId>();
        var pending = new Stack<CanvasId>();
        pending.Push(rootCanvasId);

        while (pending.Count > 0)
        {
            var canvasId = pending.Pop();
            if (!result.Add(canvasId))
                continue;

            var canvas = project.Canvases.FirstOrDefault(candidate => candidate.Id == canvasId);
            if (canvas is null)
                continue;

            foreach (var nested in canvas.Objects.OfType<CanvasDrawObject>())
                pending.Push(nested.NestedCanvasId);
        }

        return result;
    }

    private static MediaForgeRenderOutput CloneOutput(
        MediaForgeRenderOutput output,
        MediaForgePackageExportOptions options)
    {
        var clone = Clone(output);
        if (options.IncludeSecrets || clone.TypeId != RenderOutputTypes.StreamingRtmp)
            return clone;

        var sanitized = clone.Settings.DeepClone().AsObject();
        sanitized["streamKey"] = "<redacted>";
        clone.Settings = sanitized;
        return clone;
    }

    private static string CreateUniqueName(
        IEnumerable<string> existingNames,
        string baseName)
    {
        var resolvedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Imported" : baseName;
        var existing = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(resolvedBaseName))
            return resolvedBaseName;

        var index = 2;
        string candidate;
        do
        {
            candidate = $"{resolvedBaseName} {index++}";
        }
        while (existing.Contains(candidate));

        return candidate;
    }

    private static bool AreEquivalentSourceDefinitions(
        MediaForgeSourceDefinition left,
        MediaForgeSourceDefinition right) =>
        left.TypeId == right.TypeId &&
        left.SchemaVersion == right.SchemaVersion &&
        JsonNode.DeepEquals(left.Settings, right.Settings);

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, MediaForgeProjectJsonOptions.Create());
        return JsonSerializer.Deserialize<T>(json, MediaForgeProjectJsonOptions.Create())
            ?? throw new JsonException($"{typeof(T).Name} JSON deserialized to null.");
    }
}
