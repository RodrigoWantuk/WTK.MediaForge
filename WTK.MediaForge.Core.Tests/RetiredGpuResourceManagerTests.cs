using WTK.MediaForge.Core.Gpu;
using Xunit;

namespace WTK.MediaForge.Core.Tests;

public class RetiredGpuResourceManagerTests
{
    [Fact]
    public void Add_ignores_duplicate_resource()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);

        manager.Add(resource);
        manager.Add(resource);

        Assert.Equal(1, manager.PendingCount);
    }

    [Fact]
    public async Task WaitForAllFinalizedAsync_completes_when_last_lease_releases()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);

        manager.Add(resource);
        Assert.Equal(1, manager.PendingCount);

        resource.SetFinalizable(true);

        await manager.WaitForAllFinalizedAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(0, manager.PendingCount);
        Assert.True(resource.FullyDisposed.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitForAllFinalizedAsync_times_out_when_resource_never_finalizes()
    {
        var manager = new RetiredGpuResourceManager();
        manager.Add(new FakeRetiredGpuResource(finalizeResult: false));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForAllFinalizedAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None)
                .AsTask());
    }

    [Fact]
    public async Task WaitForAllFinalizedAsync_propagates_faulted_resource()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        resource.Fault(new InvalidOperationException("Finalize failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForAllFinalizedAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                .AsTask());
    }

    [Fact]
    public void Add_eagerly_finalizes_already_finalizable_resource()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: true);

        manager.Add(resource);

        Assert.Equal(0, manager.PendingCount);
        Assert.True(resource.FullyDisposed.IsCompletedSuccessfully);
    }

    private sealed class FakeRetiredGpuResource : IRetiredGpuResource
    {
        private readonly TaskCompletionSource _fullyDisposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _finalizable;

        public FakeRetiredGpuResource(bool finalizeResult)
        {
            _finalizable = finalizeResult;
        }

        public Task FullyDisposed => _fullyDisposedTcs.Task;

        public void SetFinalizable(bool finalizable) => _finalizable = finalizable;

        public void Fault(Exception exception) => _fullyDisposedTcs.TrySetException(exception);

        public bool TryFinalizePhysicalResources()
        {
            if (_fullyDisposedTcs.Task.IsCompleted)
                return _fullyDisposedTcs.Task.IsCompletedSuccessfully;

            if (!_finalizable)
                return false;

            _fullyDisposedTcs.TrySetResult();
            return true;
        }
    }
}
