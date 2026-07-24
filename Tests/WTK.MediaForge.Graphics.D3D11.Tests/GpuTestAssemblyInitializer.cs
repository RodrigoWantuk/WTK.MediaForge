using System.Runtime.CompilerServices;
using WTK.MediaForge.Testing;

internal static class GpuTestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => GpuTestProcessLease.AcquireForCurrentTestProcess();
}
