using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Composition.Tests.Gpu;

internal sealed class FakeTextureFactory : IGpuTextureFactory
{
    public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
    {
        _ = descriptor;
        return new FakePhysicalTexture();
    }

    private sealed class FakePhysicalTexture : IGpuPhysicalResource
    {
        public Task FullyDisposed => Task.CompletedTask;

        public bool TryFinalizePhysicalResources() => true;
    }
}
