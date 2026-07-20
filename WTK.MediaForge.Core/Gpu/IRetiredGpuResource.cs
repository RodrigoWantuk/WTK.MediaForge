namespace WTK.MediaForge.Core.Gpu;

public interface IRetiredGpuResource
{
    bool TryFinalizePhysicalResources();

    Task FullyDisposed { get; }
}

public interface IRetiredGpuResourceDiagnostics
{
    string DiagnosticName { get; }

    string DescribeState();
}
