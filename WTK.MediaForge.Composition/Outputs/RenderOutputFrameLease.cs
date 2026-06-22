using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class RenderOutputFrameLease : IAsyncDisposable
{
    private readonly Func<ValueTask>? _release;
    private int _disposed;

    internal RenderOutputFrameLease(RenderOutputFrameInfo info, Func<ValueTask>? release = null)
        : this(info, surfaceLease: null, release)
    {
    }

    internal RenderOutputFrameLease(
        RenderOutputFrameInfo info,
        WTK.MediaForge.Composition.Runtime.Rendering.IRenderedOutputSurfaceLease? surfaceLease,
        Func<ValueTask>? release = null)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        SurfaceLease = surfaceLease;
        _release = release;
    }

    public RenderOutputFrameInfo Info { get; }

    public RenderOutputId OutputId => Info.OutputId;

    public RenderOutputSinkId SinkId => Info.SinkId;

    public long FrameNumber => Info.FrameNumber;

    public TimeSpan Timestamp => Info.Timestamp;

    public FrameSize Size => Info.Size;

    public RenderPixelFormat Format => Info.Format;

    public RenderBackendKind BackendKind => Info.BackendKind;

    internal WTK.MediaForge.Composition.Runtime.Rendering.IRenderedOutputSurfaceLease? SurfaceLease { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_release is not null)
            await _release().ConfigureAwait(false);
    }
}
