using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsRemoteSceneSourceProviderFactory(
    IMediaForgeDiagnosticsSink? diagnostics = null) : IMediaSourceProviderFactory
{
    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.RemoteScene;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        if (!CanCreate(sourceDefinition.TypeId))
            throw new ArgumentException($"Source type '{sourceDefinition.TypeId.Value}' is not Remote Scene.", nameof(sourceDefinition));

        _ = (RemoteSceneSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);
        const string reason =
            "Remote Scene source is unavailable until the pinned libwebrtc subscriber, Media Foundation hardware packet decoder, decode-to-render, Direct, and TURN proofs pass.";
        var exception = new MediaForgeUnsupportedFeatureException(MediaForgeCapabilityCatalog.RemoteSceneSubscribe, reason);
        MediaForgeDiagnostics.Report(
            diagnostics,
            MediaForgeDiagnosticSeverity.Error,
            "source.remote_scene_unavailable",
            reason,
            nameof(WindowsRemoteSceneSourceProviderFactory),
            exception,
            sourceDefinition.Id.Value,
            sourceDefinition.Name);
        throw exception;
    }
}

