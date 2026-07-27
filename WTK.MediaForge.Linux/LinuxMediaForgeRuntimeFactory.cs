using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Linux.Media;

namespace WTK.MediaForge.Linux;

public sealed class LinuxMediaForgeRuntimeFactory : IMediaForgeRuntimeFactory
{
    public async ValueTask<MediaForgeRuntime> CreateAsync(RuntimeCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probe = new LinuxHardwareMediaCapabilityProbe();
        async ValueTask<MediaForgeCapabilitySnapshot> Snapshot(CancellationToken token)
        {
            var hardware = await probe.ProbeAsync(token).ConfigureAwait(false);
            return new MediaForgeCapabilitySnapshot
            {
                Generation = 0,
                CapturedAt = DateTimeOffset.UtcNow,
                Adapter = new MediaForgeHardwareAdapterInfo
                {
                    Platform = "Linux",
                    AdapterId = "unavailable",
                    DeviceName = "No Linux MediaForge adapter",
                    DeviceGeneration = 0
                },
                Report = MediaForgeCapabilityReportBuilder.Build(hardware,
                    MediaSourceTypeRegistry.CreateCapabilityEntries().Concat(RenderOutputTypeRegistry.CreateCapabilityEntries()))
            };
        }

        var snapshot = await Snapshot(cancellationToken).ConfigureAwait(false);
        return MediaForgeRuntime.Unavailable(
            "Linux runtime adapters are not implemented yet; no Windows or CPU fallback is available.",
            probe,
            MediaForgeRuntimeAdapterCatalog.Known,
            _ => ValueTask.FromResult(snapshot));
    }
}
