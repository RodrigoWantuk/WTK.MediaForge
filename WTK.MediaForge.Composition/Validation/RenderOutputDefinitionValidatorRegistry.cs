using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation;

public interface IRenderOutputDefinitionValidator
{
    RenderOutputTypeId TypeId { get; }

    IEnumerable<ValidationIssue> Validate(MediaForgeRenderOutput output);
}

public sealed class RenderOutputDefinitionValidatorRegistry
{
    private readonly Dictionary<string, IRenderOutputDefinitionValidator> _validators;

    public RenderOutputDefinitionValidatorRegistry(IEnumerable<IRenderOutputDefinitionValidator> validators)
    {
        _validators = validators.ToDictionary(v => v.TypeId.Value, StringComparer.Ordinal);
    }

    public static RenderOutputDefinitionValidatorRegistry Default { get; } = CreateDefault();

    public bool IsKnown(RenderOutputTypeId typeId) => RenderOutputTypeRegistry.IsKnown(typeId);

    public IEnumerable<ValidationIssue> Validate(MediaForgeRenderOutput output)
    {
        if (!_validators.TryGetValue(output.TypeId.Value, out var validator))
            yield break;

        foreach (var issue in validator.Validate(output))
            yield return issue;
    }

    private static RenderOutputDefinitionValidatorRegistry CreateDefault()
    {
        IRenderOutputDefinitionValidator[] builtIn =
        [
            new Outputs.PreviewWindowOutputDefinitionValidator(),
            new Outputs.OffscreenOutputDefinitionValidator(),
            new Outputs.NdiOutputDefinitionValidator(),
            new Outputs.EncodedFileOutputDefinitionValidator(),
            new Outputs.RecordingMp4OutputDefinitionValidator(),
            new Outputs.StreamingRtmpOutputDefinitionValidator(),
            new Outputs.StreamingSrtOutputDefinitionValidator(),
            new Outputs.StreamingRtspOutputDefinitionValidator(),
            new Outputs.StreamingHlsOutputDefinitionValidator(),
            new Outputs.VirtualCameraOutputDefinitionValidator()
        ];

        return new RenderOutputDefinitionValidatorRegistry(builtIn);
    }
}
