namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanOffscreenTargetHandle : IDisposable
{
    private IVulkanOffscreenRenderTarget? _target;
    private int _submissionRefs;
    private int _retired;

    public VulkanOffscreenTargetHandle(IVulkanOffscreenRenderTarget target) =>
        _target = target ?? throw new ArgumentNullException(nameof(target));

    public IVulkanOffscreenRenderTarget Target =>
        _target ?? throw new ObjectDisposedException(nameof(VulkanOffscreenTargetHandle));

    public bool IsAlive => _target is not null;

    public bool HasSubmissionReferences => Volatile.Read(ref _submissionRefs) > 0;

    public void RetainForSubmission()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        Interlocked.Increment(ref _submissionRefs);
    }

    public void ReleaseSubmissionReference()
    {
        var remaining = Interlocked.Decrement(ref _submissionRefs);
        if (remaining < 0)
        {
            Interlocked.Increment(ref _submissionRefs);
            throw new InvalidOperationException(
                "Offscreen target submission reference released more times than retained.");
        }

        if (remaining == 0 && Volatile.Read(ref _retired) != 0)
            DisposeTarget();
    }

    public void Retire()
    {
        Volatile.Write(ref _retired, 1);
        if (Volatile.Read(ref _submissionRefs) == 0)
            DisposeTarget();
    }

    public void Dispose() => Retire();

    private void DisposeTarget()
    {
        var target = Interlocked.Exchange(ref _target, null);
        target?.Dispose();
    }
}
