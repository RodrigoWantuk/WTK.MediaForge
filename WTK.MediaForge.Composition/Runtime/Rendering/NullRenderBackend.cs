using System.Collections.Concurrent;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class NullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly ConcurrentDictionary<RenderOutputId, RenderOutputBindingSnapshot> _bindings = new();

    public NullRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int RenderCount => Volatile.Read(ref _renderCount);

    public long LastProjectStateVersion => Volatile.Read(ref _lastProjectStateVersion);

    private int _renderCount;
    private long _lastProjectStateVersion;

    public IReadOnlyDictionary<RenderOutputId, RenderOutputBindingSnapshot> Bindings => _bindings;

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(binding);
        _bindings[binding.OutputId] = binding;
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        _threadGuard.AssertOnRenderThread();
        _bindings.TryRemove(outputId, out _);
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        _threadGuard.AssertOnRenderThread();

        if (_bindings.TryGetValue(outputId, out var existing))
        {
            _bindings[outputId] = new RenderOutputBindingSnapshot
            {
                OutputId = existing.OutputId,
                TargetKind = existing.TargetKind,
                NativeHandle = existing.NativeHandle,
                SurfaceSize = surfaceSize,
                BindingVersion = existing.BindingVersion + 1
            };
        }
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _renderCount);
        Volatile.Write(ref _lastProjectStateVersion, snapshot.ProjectStateVersion);
        return new ImmediateRenderFrameSubmission(snapshot);
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
