namespace WTK.MediaForge.Composition.Outputs;

public interface IRenderOutputSink : IAsyncDisposable
{
    RenderOutputSinkId Id { get; }

    RenderOutputSinkKind Kind { get; }

    RenderOutputSinkBackpressureMode BackpressureMode { get; }

    ValueTask StartAsync(
        RenderOutputSinkContext context,
        CancellationToken cancellationToken);

    ValueTask OnFrameAsync(
        RenderOutputFrameLease frame,
        CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
