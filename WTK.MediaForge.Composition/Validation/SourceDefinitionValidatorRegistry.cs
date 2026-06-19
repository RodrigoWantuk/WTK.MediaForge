using WTK.MediaForge.Composition.Project;
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

    public bool IsKnown(MediaSourceTypeId typeId) =>
        !typeId.IsEmpty && _validators.ContainsKey(typeId.Value);

    public IEnumerable<ValidationIssue> Validate(MediaForgeSourceDefinition source)
    {
        if (!_validators.TryGetValue(source.TypeId.Value, out var validator))
            yield break;

        foreach (var issue in validator.Validate(source))
            yield return issue;
    }

    private static SourceDefinitionValidatorRegistry CreateDefault()
    {
        ISourceDefinitionValidator[] builtIn =
        [
            new BuiltinSourceDefinitionValidator(MediaSourceTypeId.DesktopCapture),
            new BuiltinSourceDefinitionValidator(MediaSourceTypeId.ImageFile),
            new BuiltinSourceDefinitionValidator(MediaSourceTypeId.VideoFile)
        ];

        return new SourceDefinitionValidatorRegistry(builtIn);
    }

    private sealed class BuiltinSourceDefinitionValidator : ISourceDefinitionValidator
    {
        public BuiltinSourceDefinitionValidator(MediaSourceTypeId typeId) =>
            TypeId = typeId;

        public MediaSourceTypeId TypeId { get; }

        public IEnumerable<ValidationIssue> Validate(MediaForgeSourceDefinition source) =>
            Enumerable.Empty<ValidationIssue>();
    }
}
