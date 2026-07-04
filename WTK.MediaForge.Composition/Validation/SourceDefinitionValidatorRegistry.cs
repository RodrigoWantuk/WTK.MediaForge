using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Validation;

public interface ISourceDefinitionValidator
{
    MediaSourceTypeId TypeId { get; }

    IEnumerable<ValidationIssue> Validate(MediaForgeSourceDefinition source);
}

public sealed class SourceDefinitionValidatorRegistry
{
    private readonly Dictionary<string, ISourceDefinitionValidator> _validators;

    public SourceDefinitionValidatorRegistry(IEnumerable<ISourceDefinitionValidator> validators)
    {
        _validators = validators.ToDictionary(v => v.TypeId.Value, StringComparer.Ordinal);
    }

    public static SourceDefinitionValidatorRegistry Default { get; } = CreateDefault();

    public bool IsKnown(MediaSourceTypeId typeId) => MediaSourceTypeRegistry.IsKnown(typeId);

    public IEnumerable<ValidationIssue> Validate(MediaForgeSourceDefinition source)
    {
        var canonical = MediaSourceTypeRegistry.ResolveCanonical(source.TypeId);
        if (!_validators.TryGetValue(canonical.Value, out var validator))
            yield break;

        foreach (var issue in validator.Validate(source))
            yield return issue;
    }

    private static SourceDefinitionValidatorRegistry CreateDefault()
    {
        ISourceDefinitionValidator[] builtIn =
        [
            new Sources.DesktopCaptureSourceDefinitionValidator(),
            new Sources.WebcamSourceDefinitionValidator(),
            new Sources.NdiInputSourceDefinitionValidator(),
            new Sources.RtspInputSourceDefinitionValidator(),
            new Sources.IpCameraSourceDefinitionValidator(),
            new Sources.VideoFileSourceDefinitionValidator(),
            new Sources.ImageFileSourceDefinitionValidator(),
            new Sources.AnimatedImageSourceDefinitionValidator(),
            new Sources.LottieSourceDefinitionValidator(),
            new Sources.WindowCaptureSourceDefinitionValidator(),
            new Sources.GeneratedSourceDefinitionValidator()
        ];

        return new SourceDefinitionValidatorRegistry(builtIn);
    }
}
