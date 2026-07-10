using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

public interface IEncodedPacketSink : IAsyncDisposable
{
    ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken);

    ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
