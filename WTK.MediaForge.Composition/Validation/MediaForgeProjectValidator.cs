using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation.Effects;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation;

public static class MediaForgeProjectValidator
{
    private static readonly SourceDefinitionValidatorRegistry SourceRegistry =
        SourceDefinitionValidatorRegistry.Default;

    private static readonly RenderOutputDefinitionValidatorRegistry OutputRegistry =
        RenderOutputDefinitionValidatorRegistry.Default;

    public static ProjectValidationResult Validate(MediaForgeProject project)
    {
        var issues = new List<ValidationIssue>();

        if (project.SchemaVersion <= 0)
            issues.Add(ValidationIssue.Error("project.schema.invalid", "SchemaVersion must be positive."));

        ValidateUniqueIds(project, issues);

        var canvasIds = project.Canvases.Select(c => c.Id).ToHashSet();
        var sourceIds = project.SourceDefinitions.Select(s => s.Id).ToHashSet();

        ValidateCanvases(project, canvasIds, sourceIds, issues);
        issues.AddRange(CanvasGraphValidator.Validate(project));
        ValidateSourceDefinitions(project, sourceIds, issues);
        ValidateOutputs(project, canvasIds, issues);

        return new ProjectValidationResult(issues);
    }

    private static void ValidateUniqueIds(MediaForgeProject project, List<ValidationIssue> issues)
    {
        CheckDuplicateIds(project.SourceDefinitions.Select(s => s.Id.Value), "source", issues);
        CheckDuplicateIds(project.Canvases.Select(c => c.Id.Value), "canvas", issues);
        CheckDuplicateIds(project.Outputs.Select(o => o.Id.Value), "output", issues);

        foreach (var canvas in project.Canvases)
            CheckDuplicateIds(canvas.Objects.Select(o => o.Id.Value), $"canvas '{canvas.Name}' draw object", issues);
    }

    private static void CheckDuplicateIds(IEnumerable<Guid> ids, string label, List<ValidationIssue> issues)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty)
                issues.Add(ValidationIssue.Error($"{label}.id.empty", $"{label} contains Guid.Empty."));

            if (!seen.Add(id))
                issues.Add(ValidationIssue.Error($"{label}.id.duplicate", $"Duplicate id in {label}: {id}."));
        }
    }

    private static void ValidateCanvases(
        MediaForgeProject project,
        HashSet<CanvasId> canvasIds,
        HashSet<SourceId> sourceIds,
        List<ValidationIssue> issues)
    {
        foreach (var canvas in project.Canvases)
        {
            if (canvas.Id.IsEmpty)
                issues.Add(ValidationIssue.Error("canvas.id.empty", "Canvas has empty id."));

            if (canvas.Size.IsEmpty)
                issues.Add(ValidationIssue.Error("canvas.size.invalid", $"Canvas '{canvas.Name}' has invalid size."));

            if (!canvas.BackgroundColor.IsInRange())
                issues.Add(ValidationIssue.Error("canvas.background.invalid", $"Canvas '{canvas.Name}' background color out of range."));

            issues.AddRange(EffectValidation.ValidateStack(
                canvas.Effects,
                EffectScope.Canvas,
                canvas.Name,
                "project canvas"));

            foreach (var drawObject in canvas.Objects)
                ValidateDrawObject(drawObject, canvas.Name, sourceIds, canvasIds, issues);
        }
    }

    private static void ValidateSourceDefinitions(
        MediaForgeProject project,
        HashSet<SourceId> sourceIds,
        List<ValidationIssue> issues)
    {
        foreach (var source in project.SourceDefinitions)
        {
            if (source.Id.IsEmpty)
                issues.Add(ValidationIssue.Error("source.id.empty", "Source definition has empty id."));

            if (source.TypeId.IsEmpty || !SourceRegistry.IsKnown(source.TypeId))
                issues.Add(ValidationIssue.Error("source.type.invalid", $"Unknown or empty source type id: '{source.TypeId.Value}'."));

            if (source.SchemaVersion <= 0)
                issues.Add(ValidationIssue.Error("source.schema.invalid", $"Source '{source.Name}' has invalid SchemaVersion."));

            issues.AddRange(EffectValidation.ValidateStack(
                source.Effects,
                EffectScope.Source,
                source.Name,
                "project source"));

            issues.AddRange(SourceRegistry.Validate(source));
        }
    }

    private static void ValidateOutputs(
        MediaForgeProject project,
        HashSet<CanvasId> canvasIds,
        List<ValidationIssue> issues)
    {
        foreach (var output in project.Outputs)
        {
            if (output.Id.IsEmpty)
                issues.Add(ValidationIssue.Error("output.id.empty", "Output has empty id."));

            if (output.TypeId.IsEmpty || !OutputRegistry.IsKnown(output.TypeId))
                issues.Add(ValidationIssue.Error("output.type.invalid", $"Unknown or empty output type id: '{output.TypeId.Value}'."));

            if (output.SchemaVersion <= 0)
                issues.Add(ValidationIssue.Error("output.schema.invalid", $"Output '{output.Name}' has invalid SchemaVersion."));

            issues.AddRange(OutputRegistry.Validate(output));

            if (output.CanvasId.IsEmpty)
                issues.Add(ValidationIssue.Error("output.canvas.empty", $"Output '{output.Name}' has empty CanvasId."));
            else if (!canvasIds.Contains(output.CanvasId))
                issues.Add(ValidationIssue.Error("output.canvas.missing", $"Output '{output.Name}' references missing canvas {output.CanvasId}."));

            if (output.OutputSize.IsEmpty)
                issues.Add(ValidationIssue.Error("output.size.invalid", $"Output '{output.Name}' has invalid OutputSize."));

            if (!output.LetterboxColor.IsInRange())
                issues.Add(ValidationIssue.Error("output.letterbox.invalid", $"Output '{output.Name}' letterbox color out of range."));
        }
    }

    private static void ValidateDrawObject(
        MediaForgeDrawObject drawObject,
        string canvasName,
        HashSet<SourceId> sourceIds,
        HashSet<CanvasId> canvasIds,
        List<ValidationIssue> issues)
    {
        if (drawObject.Id.IsEmpty)
            issues.Add(ValidationIssue.Error("drawobject.id.empty", $"Draw object in canvas '{canvasName}' has empty id."));

        ValidateTransform(drawObject, canvasName, issues);

        if (!IsValidOpacity(drawObject.Opacity))
            issues.Add(ValidationIssue.Error("drawobject.opacity.invalid", $"Draw object '{drawObject.Name}' opacity out of range."));

        if (drawObject.Crop is { } crop && !crop.IsValid)
            issues.Add(ValidationIssue.Error("drawobject.crop.invalid", $"Draw object '{drawObject.Name}' has invalid crop."));

        issues.AddRange(EffectValidation.ValidateDrawObjectEffects(drawObject, canvasName));

        switch (drawObject)
        {
            case SourceLayerDrawObject sourceLayer:
                if (sourceLayer.SourceId.IsEmpty)
                    issues.Add(ValidationIssue.Error("drawobject.source.empty", $"Source layer '{drawObject.Name}' has empty SourceId."));
                else if (!sourceIds.Contains(sourceLayer.SourceId))
                    issues.Add(ValidationIssue.Error("drawobject.source.missing", $"Source layer '{drawObject.Name}' references missing source {sourceLayer.SourceId}."));
                else if (!sourceLayer.LetterboxColor.IsInRange())
                    issues.Add(ValidationIssue.Error("drawobject.source.letterbox", $"Source layer '{drawObject.Name}' letterbox color out of range."));
                break;

            case TextDrawObject text:
                if (string.IsNullOrWhiteSpace(text.FontFamily))
                    issues.Add(ValidationIssue.Error("drawobject.text.font_family", $"Text object '{drawObject.Name}' has invalid FontFamily."));
                if (!text.TextColor.IsInRange())
                    issues.Add(ValidationIssue.Error("drawobject.text.color", $"Text object '{drawObject.Name}' color out of range."));
                if (!float.IsFinite(text.FontSize) || text.FontSize <= 0)
                    issues.Add(ValidationIssue.Error("drawobject.text.font", $"Text object '{drawObject.Name}' has invalid FontSize."));
                break;

            case SolidDrawObject solid:
                if (!solid.FillColor.IsInRange())
                    issues.Add(ValidationIssue.Error("drawobject.solid.color", $"Solid object '{drawObject.Name}' fill color out of range."));
                break;

            case CanvasDrawObject nested:
                if (nested.NestedCanvasId.IsEmpty)
                    issues.Add(ValidationIssue.Error("drawobject.canvas.empty", $"Canvas object '{drawObject.Name}' has empty NestedCanvasId."));
                else if (!canvasIds.Contains(nested.NestedCanvasId))
                    issues.Add(ValidationIssue.Error("drawobject.canvas.missing", $"Canvas object '{drawObject.Name}' references missing canvas {nested.NestedCanvasId}."));

                try
                {
                    nested.VersionBinding.Validate();
                }
                catch (Exception ex)
                {
                    issues.Add(ValidationIssue.Error(
                        "drawobject.canvas.version_binding",
                        $"Canvas object '{drawObject.Name}' has invalid scene version binding: {ex.Message}"));
                }
                break;

            case AdjustmentLayerDrawObject adjustment:
                if (!Enum.IsDefined(adjustment.TargetMode))
                {
                    issues.Add(ValidationIssue.Error(
                        "drawobject.adjustment.target_mode",
                        $"Adjustment layer '{drawObject.Name}' has an invalid target mode."));
                }

                issues.AddRange(EffectValidation.ValidateMask(
                    adjustment.Mask,
                    "Adjustment layer",
                    drawObject.Name,
                    canvasName));
                break;
        }
    }

    private static void ValidateTransform(MediaForgeDrawObject drawObject, string canvasName, List<ValidationIssue> issues)
    {
        var transform = drawObject.Transform;

        if (!IsValidCanvasPoint(transform.Position))
            issues.Add(ValidationIssue.Error("drawobject.transform.position", $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has invalid position."));

        if (!IsValidCanvasSize(transform.Size))
            issues.Add(ValidationIssue.Error("drawobject.transform.size", $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has non-positive size."));

        if (!float.IsFinite(transform.RotationDegrees))
            issues.Add(ValidationIssue.Error("drawobject.transform.rotation", $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has invalid rotation."));

        if (!IsValidNormalizedPoint(transform.Pivot))
            issues.Add(ValidationIssue.Error("drawobject.transform.pivot", $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has invalid pivot."));
    }

    private static bool IsValidCanvasPoint(CanvasPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static bool IsValidCanvasSize(CanvasSize size) =>
        float.IsFinite(size.Width) &&
        float.IsFinite(size.Height) &&
        size.Width > 0 &&
        size.Height > 0;

    private static bool IsValidNormalizedPoint(NormalizedPoint point) =>
        float.IsFinite(point.X) &&
        float.IsFinite(point.Y) &&
        point.X >= 0f &&
        point.X <= 1f &&
        point.Y >= 0f &&
        point.Y <= 1f;

    private static bool IsValidOpacity(float value) =>
        float.IsFinite(value) && value >= 0f && value <= 1f;
}
