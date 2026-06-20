namespace WTK.MediaForge.Core.Gpu;

public interface IRetiredGpuResource
{
    bool TryFinalizePhysicalResources();

    Task FullyDisposed { get; }
}
