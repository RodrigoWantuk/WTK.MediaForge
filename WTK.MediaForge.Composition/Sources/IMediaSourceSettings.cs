using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources;

public interface IMediaSourceSettings
{
    MediaSourceTypeId TypeId { get; }

    int SchemaVersion { get; }
}
