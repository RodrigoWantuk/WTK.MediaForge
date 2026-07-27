using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Runtime;

public sealed class RuntimeCreationRequest;

public enum MediaForgeRuntimeAvailability
{
    Available = 0,
    Unavailable = 1
}

public sealed record MediaForgeRuntimeAdapterCatalog(
    IReadOnlyList<MediaSourceTypeId> SourceTypes,
    IReadOnlyList<RenderOutputTypeId> OutputTypes)
{
    public static MediaForgeRuntimeAdapterCatalog Known { get; } = new(
        [MediaSourceTypes.Desktop, MediaSourceTypes.Webcam, MediaSourceTypes.NdiInput,
         MediaSourceTypes.RtspInput, MediaSourceTypes.IpCamera, MediaSourceTypes.VideoFile,
         MediaSourceTypes.ImageFile, MediaSourceTypes.AnimatedImage, MediaSourceTypes.Lottie,
         MediaSourceTypes.WindowCapture, MediaSourceTypes.Generated, MediaSourceTypes.RemoteScene],
        [RenderOutputTypes.PreviewWindow, RenderOutputTypes.Offscreen, RenderOutputTypes.Ndi,
         RenderOutputTypes.EncodedFile, RenderOutputTypes.RecordingMp4, RenderOutputTypes.StreamingRtmp,
         RenderOutputTypes.StreamingSrt, RenderOutputTypes.StreamingRtsp, RenderOutputTypes.StreamingHls,
         RenderOutputTypes.VirtualCamera, RenderOutputTypes.RemoteScene]);
}

public interface IMediaForgeRuntimeFactory
{
    ValueTask<MediaForgeRuntime> CreateAsync(RuntimeCreationRequest request, CancellationToken cancellationToken = default);
}

public sealed class MediaForgeRuntime : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<MediaForgeCapabilitySnapshot>> _getCapabilities;

    private MediaForgeRuntime(
        MediaForgeRuntimeAvailability availability,
        string? unavailableReason,
        MediaForgeEngine? engine,
        IHardwareMediaCapabilityProbe capabilityProbe,
        MediaForgeRuntimeAdapterCatalog adapters,
        Func<CancellationToken, ValueTask<MediaForgeCapabilitySnapshot>> getCapabilities)
    {
        Availability = availability;
        UnavailableReason = unavailableReason;
        Engine = engine;
        CapabilityProbe = capabilityProbe;
        Adapters = adapters;
        _getCapabilities = getCapabilities;
    }

    public MediaForgeRuntimeAvailability Availability { get; }
    public string? UnavailableReason { get; }
    public MediaForgeEngine? Engine { get; }
    public IHardwareMediaCapabilityProbe CapabilityProbe { get; }
    public MediaForgeRuntimeAdapterCatalog Adapters { get; }

    public static MediaForgeRuntime Available(
        MediaForgeEngine engine,
        IHardwareMediaCapabilityProbe capabilityProbe,
        MediaForgeRuntimeAdapterCatalog adapters,
        Func<CancellationToken, ValueTask<MediaForgeCapabilitySnapshot>> getCapabilities) =>
        new(MediaForgeRuntimeAvailability.Available, null, engine, capabilityProbe, adapters, getCapabilities);

    public static MediaForgeRuntime Unavailable(
        string reason,
        IHardwareMediaCapabilityProbe capabilityProbe,
        MediaForgeRuntimeAdapterCatalog adapters,
        Func<CancellationToken, ValueTask<MediaForgeCapabilitySnapshot>> getCapabilities) =>
        new(MediaForgeRuntimeAvailability.Unavailable, reason, null, capabilityProbe, adapters, getCapabilities);

    public ValueTask<MediaForgeCapabilitySnapshot> GetCapabilitySnapshotAsync(CancellationToken cancellationToken = default) =>
        _getCapabilities(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Engine is not null)
            await Engine.DisposeAsync().ConfigureAwait(false);
    }
}
