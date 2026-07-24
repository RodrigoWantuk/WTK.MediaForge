using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Capture.Tests;

internal sealed class ManualNullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly List<ManualRenderFrameSubmission> _pending = [];
    private int _submitCount;

    public ManualNullRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int SubmitCount => Volatile.Read(ref _submitCount);

    public void BindOutput(RenderOutputBindingSnapshot binding) { }

    public void UnbindOutput(RenderOutputId outputId) { }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        var submission = new ManualRenderFrameSubmission(snapshot);

        lock (_pending)
            _pending.Add(submission);

        Interlocked.Increment(ref _submitCount);

        return submission;
    }

    public void CompleteAllPending()
    {
        ManualRenderFrameSubmission[] copy;

        lock (_pending)
        {
            copy = [.. _pending];
            _pending.Clear();
        }

        foreach (var submission in copy)
            submission.Complete();
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}
