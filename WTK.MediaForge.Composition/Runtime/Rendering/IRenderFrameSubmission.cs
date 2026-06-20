namespace WTK.MediaForge.Composition.Runtime.Rendering;

public interface IRenderFrameSubmission : IDisposable
{
    /// <summary>
    /// Non-blocking completion probe. Must not wait on GPU work.
    /// </summary>
    bool IsCompleted { get; }
}
