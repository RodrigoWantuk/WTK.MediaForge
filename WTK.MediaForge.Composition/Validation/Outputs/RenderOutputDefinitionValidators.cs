using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation.Outputs;

internal static class OutputSettingsValidation
{
    public static IEnumerable<ValidationIssue> ValidateSchemaVersion(
        IRenderOutputSettings settings,
        string outputName)
    {
        if (settings.SchemaVersion <= 0)
        {
            yield return ValidationIssue.Error(
                "output.schema.invalid",
                $"Output '{outputName}' has invalid settings SchemaVersion.");
        }
    }

    public static IEnumerable<ValidationIssue> ValidateNonEmptyString(
        string value,
        string outputName,
        string code,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return ValidationIssue.Error(
                code,
                $"Output '{outputName}' requires a non-empty {fieldName}.");
        }
    }
}

internal sealed class PreviewWindowOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<PreviewWindowOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        PreviewWindowOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;
    }
}

internal sealed class OffscreenOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<OffscreenOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.Offscreen;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        OffscreenOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;
    }
}

internal sealed class NdiOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<NdiOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.Ndi;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        NdiOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.SourceName, output.Name, "output.ndi.name", "SourceName"))
            yield return issue;
    }
}

internal sealed class RecordingMp4OutputDefinitionValidator : TypedRenderOutputDefinitionValidator<RecordingMp4OutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.RecordingMp4;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        RecordingMp4OutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Path, output.Name, "output.recording.path", "Path"))
            yield return issue;
    }
}

internal sealed class EncodedFileOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<EncodedFileOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.EncodedFile;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        EncodedFileOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Path, output.Name, "output.encoded.path", "Path"))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Container, output.Name, "output.encoded.container", "Container"))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.VideoCodec, output.Name, "output.encoded.video_codec", "VideoCodec"))
            yield return issue;
    }
}

internal sealed class StreamingRtmpOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<StreamingRtmpOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.StreamingRtmp;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        StreamingRtmpOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Url, output.Name, "output.rtmp.url", "Url"))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.StreamKey, output.Name, "output.rtmp.key", "StreamKey"))
            yield return issue;
    }
}

internal sealed class StreamingSrtOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<StreamingSrtOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.StreamingSrt;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        StreamingSrtOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Url, output.Name, "output.srt.url", "Url"))
            yield return issue;
    }
}

internal sealed class StreamingRtspOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<StreamingRtspOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.StreamingRtsp;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        StreamingRtspOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Url, output.Name, "output.rtsp.url", "Url"))
            yield return issue;
    }
}

internal sealed class StreamingHlsOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<StreamingHlsOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.StreamingHls;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        StreamingHlsOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.Path, output.Name, "output.hls.path", "Path"))
            yield return issue;
    }
}

internal sealed class VirtualCameraOutputDefinitionValidator : TypedRenderOutputDefinitionValidator<VirtualCameraOutputSettings>
{
    public override RenderOutputTypeId TypeId => RenderOutputTypes.VirtualCamera;

    protected override IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        VirtualCameraOutputSettings settings)
    {
        foreach (var issue in OutputSettingsValidation.ValidateSchemaVersion(settings, output.Name))
            yield return issue;

        foreach (var issue in OutputSettingsValidation.ValidateNonEmptyString(settings.DeviceName, output.Name, "output.virtualcamera.name", "DeviceName"))
            yield return issue;
    }
}

internal abstract class TypedRenderOutputDefinitionValidator<TSettings> : IRenderOutputDefinitionValidator
    where TSettings : class, IRenderOutputSettings
{
    public abstract RenderOutputTypeId TypeId { get; }

    public IEnumerable<ValidationIssue> Validate(MediaForgeRenderOutput output)
    {
        if (!RenderOutputSettingsSerializer.TryDeserialize(output.TypeId, output.Settings, out var settings, out var issue))
        {
            if (issue is not null)
                yield return issue;

            yield break;
        }

        if (settings is not TSettings typed)
        {
            yield return ValidationIssue.Error(
                "output.settings.type_mismatch",
                $"Output '{output.Name}' settings do not match type '{TypeId.Value}'.");
            yield break;
        }

        foreach (var typedIssue in ValidateSettings(output, typed))
            yield return typedIssue;
    }

    protected abstract IEnumerable<ValidationIssue> ValidateSettings(
        MediaForgeRenderOutput output,
        TSettings settings);
}
