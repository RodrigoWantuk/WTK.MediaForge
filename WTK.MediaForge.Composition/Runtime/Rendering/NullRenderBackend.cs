using System.Collections.Concurrent;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class NullRenderBackend : IRenderBackend
{
    private readonly ConcurrentDictionary<RenderOutputId, RenderOutputBindingSnapshot> _bindings = new();

    public int RenderCount => Volatile.Read(ref _renderCount);

    public long LastProjectStateVersion => Volatile.Read(ref _lastProjectStateVersion);

    private int _renderCount;
    private long _lastProjectStateVersion;

    public IReadOnlyDictionary<RenderOutputId, RenderOutputBindingSnapshot> Bindings => _bindings;

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        RenderThreadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(binding);
        _bindings[binding.OutputId] = binding;
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        RenderThreadGuard.AssertOnRenderThread();
        _bindings.TryRemove(outputId, out _);
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        RenderThreadGuard.AssertOnRenderThread();

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

    public void Render(RenderFrameSnapshot snapshot)
    {
        RenderThreadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _renderCount);
        Volatile.Write(ref _lastProjectStateVersion, snapshot.ProjectStateVersion);
        snapshot.Dispose();
    }
}
