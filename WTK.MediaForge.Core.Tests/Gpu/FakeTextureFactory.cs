using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Core.Tests.Gpu;

internal sealed class FakeTextureFactory : IGpuTextureFactory
{
    private readonly bool _faultOnFinalize;

    public FakeTextureFactory(bool faultOnFinalize = false) =>
        _faultOnFinalize = faultOnFinalize;

    public int CreateCount { get; private set; }

    public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
    {
        _ = descriptor;
        CreateCount++;
        return new FakePhysicalTexture(_faultOnFinalize);
    }

    private sealed class FakePhysicalTexture : IGpuPhysicalResource
    {
        private readonly bool _faultOnFinalize;
        private readonly TaskCompletionSource _fullyDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finalized;

        public FakePhysicalTexture(bool faultOnFinalize) =>
            _faultOnFinalize = faultOnFinalize;

        public Task FullyDisposed => _fullyDisposed.Task;

        public bool TryFinalizePhysicalResources()
        {
            if (Interlocked.Exchange(ref _finalized, 1) != 0)
                return _fullyDisposed.Task.IsCompleted;

            if (_faultOnFinalize)
            {
                var ex = new InvalidOperationException("Simulated physical dispose failure.");
                _fullyDisposed.TrySetException(ex);
                throw ex;
            }

            _fullyDisposed.TrySetResult();
            return true;
        }
    }
}
