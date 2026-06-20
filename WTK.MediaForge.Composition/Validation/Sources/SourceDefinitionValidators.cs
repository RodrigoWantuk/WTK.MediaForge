using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation.Sources;

internal static class SourceSettingsValidation
{
    public static IEnumerable<ValidationIssue> ValidateSchemaVersion(
        IMediaSourceSettings settings,
        string sourceName)
    {
        if (settings.SchemaVersion <= 0)
        {
            yield return ValidationIssue.Error(
                "source.schema.invalid",
                $"Source '{sourceName}' has invalid settings SchemaVersion.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidateNonEmptyPath(string path, string sourceName, string fieldCode)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield return ValidationIssue.Error(
                fieldCode,
                $"Source '{sourceName}' requires a non-empty file path.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidateNonNegative(int value, string sourceName, string fieldCode, string fieldName)
    {
        if (value < 0)
        {
            yield return ValidationIssue.Error(
                fieldCode,
                $"Source '{sourceName}' {fieldName} must be non-negative.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidatePositiveOptional(int? value, string sourceName, string fieldCode, string fieldName)
    {
        if (value is <= 0)
        {
            yield return ValidationIssue.Error(
                fieldCode,
                $"Source '{sourceName}' {fieldName} must be positive when specified.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidatePositiveOptional(double? value, string sourceName, string fieldCode, string fieldName)
    {
        if (value is <= 0 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
        {
            yield return ValidationIssue.Error(
                fieldCode,
                $"Source '{sourceName}' {fieldName} must be a positive finite number when specified.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidateNonEmptyString(string value, string sourceName, string fieldCode, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return ValidationIssue.Error(
                fieldCode,
                $"Source '{sourceName}' requires a non-empty {fieldName}.");
        }
    }
}

internal sealed class DesktopCaptureSourceDefinitionValidator : TypedSourceDefinitionValidator<DesktopCaptureSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.Desktop;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        DesktopCaptureSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonNegative(settings.AdapterIndex, source.Name, "source.desktop.adapter", "AdapterIndex"))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonNegative(settings.OutputIndex, source.Name, "source.desktop.output", "OutputIndex"))
            yield return issue;
    }
}

internal sealed class WebcamSourceDefinitionValidator : TypedSourceDefinitionValidator<WebcamSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.Webcam;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        WebcamSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyString(settings.DeviceId, source.Name, "source.webcam.device", "DeviceId"))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidatePositiveOptional(settings.PreferredWidth, source.Name, "source.webcam.width", "PreferredWidth"))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidatePositiveOptional(settings.PreferredHeight, source.Name, "source.webcam.height", "PreferredHeight"))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidatePositiveOptional(settings.PreferredFrameRate, source.Name, "source.webcam.framerate", "PreferredFrameRate"))
            yield return issue;
    }
}

internal sealed class NdiInputSourceDefinitionValidator : TypedSourceDefinitionValidator<NdiInputSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.NdiInput;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        NdiInputSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyString(settings.SourceName, source.Name, "source.ndi.name", "SourceName"))
            yield return issue;
    }
}

internal sealed class RtspInputSourceDefinitionValidator : TypedSourceDefinitionValidator<RtspInputSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.RtspInput;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        RtspInputSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyString(settings.Url, source.Name, "source.rtsp.url", "Url"))
            yield return issue;
    }
}

internal sealed class VideoFileSourceDefinitionValidator : TypedSourceDefinitionValidator<VideoFileSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.VideoFile;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        VideoFileSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyPath(settings.Path, source.Name, "source.video.path"))
            yield return issue;
    }
}

internal sealed class ImageFileSourceDefinitionValidator : TypedSourceDefinitionValidator<ImageFileSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.ImageFile;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        ImageFileSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyPath(settings.Path, source.Name, "source.image.path"))
            yield return issue;
    }
}

internal sealed class WindowCaptureSourceDefinitionValidator : TypedSourceDefinitionValidator<WindowCaptureSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.WindowCapture;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        WindowCaptureSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        if (settings.WindowHandle == 0)
        {
            yield return ValidationIssue.Error(
                "source.window.handle",
                $"Source '{source.Name}' requires a non-zero WindowHandle.");
        }
    }
}

internal sealed class GeneratedSourceDefinitionValidator : TypedSourceDefinitionValidator<GeneratedSourceSettings>
{
    public override MediaSourceTypeId TypeId => MediaSourceTypes.Generated;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        GeneratedSourceSettings settings)
    {
        foreach (var issue in SourceSettingsValidation.ValidateSchemaVersion(settings, source.Name))
            yield return issue;

        foreach (var issue in SourceSettingsValidation.ValidateNonEmptyString(settings.GeneratorKind, source.Name, "source.generated.kind", "GeneratorKind"))
            yield return issue;
    }
}

internal abstract class TypedSourceDefinitionValidator<TSettings> : ISourceDefinitionValidator
    where TSettings : class, IMediaSourceSettings
{
    public abstract MediaSourceTypeId TypeId { get; }

    public IEnumerable<ValidationIssue> Validate(MediaForgeSourceDefinition source)
    {
        if (!MediaSourceSettingsSerializer.TryDeserialize(source.TypeId, source.Settings, out var settings, out var issue))
        {
            if (issue is not null)
                yield return issue;

            yield break;
        }

        if (settings is not TSettings typed)
        {
            yield return ValidationIssue.Error(
                "source.settings.type_mismatch",
                $"Source '{source.Name}' settings do not match type '{TypeId.Value}'.");
            yield break;
        }

        foreach (var typedIssue in ValidateSettings(source, typed))
            yield return typedIssue;
    }

    protected abstract IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeSourceDefinition source,
        TSettings settings);
}
