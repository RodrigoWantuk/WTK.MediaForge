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
        manager.Add(new DiagnosticRetiredGpuResource("capture-ring-42", "refs=2"));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForAllFinalizedAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None)
                .AsTask());

        Assert.Contains("capture-ring-42", exception.Message, StringComparison.Ordinal);
        Assert.Contains("refs=2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempts=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForAllFinalizedAsync_propagates_faulted_resource()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        resource.Fault(new InvalidOperationException("Finalize failed"));
        manager.TryFinalizeAll();

        Assert.Equal(0, manager.PendingCount);
        Assert.Equal(1, manager.FailedCount);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            manager.WaitForAllFinalizedAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                .AsTask());

        Assert.Contains(ex.InnerExceptions, inner => inner is InvalidOperationException);
    }

    [Fact]
    public void Faulted_resource_moves_to_failed_resources()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        resource.Fault(new InvalidOperationException("Finalize failed"));
        manager.TryFinalizeAll();

        Assert.Equal(0, manager.PendingCount);
        Assert.Equal(1, manager.FailedCount);
        Assert.Same(resource, manager.Failures[0].Resource);
    }

    [Fact]
    public void Faulted_resource_does_not_keep_pending_count_nonzero()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        resource.Fault(new InvalidOperationException("Finalize failed"));
        manager.TryFinalizeAll();

        Assert.Equal(0, manager.PendingCount);
    }

    [Fact]
    public void Failed_resources_are_observable_after_fault()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        var fault = new InvalidOperationException("Finalize failed");
        resource.Fault(fault);
        manager.TryFinalizeAll();

        var failure = Assert.Single(manager.Failures);
        Assert.Same(resource, failure.Resource);
        Assert.Same(fault, failure.Exception);
    }

    [Fact]
    public void Faulted_resource_is_not_added_to_failures_twice()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new FakeRetiredGpuResource(finalizeResult: false);
        manager.Add(resource);

        resource.Fault(new InvalidOperationException("Finalize failed"));
        manager.TryFinalizeAll();
        manager.TryFinalizeAll();

        Assert.Equal(1, manager.FailedCount);
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

    [Fact]
    public async Task Finalization_exception_is_recorded_even_when_resource_does_not_fault_completion_task()
    {
        var manager = new RetiredGpuResourceManager();
        var resource = new ThrowingRetiredGpuResource();

        manager.Add(resource);

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            manager.WaitForAllFinalizedAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                .AsTask());

        Assert.Contains(exception.InnerExceptions, inner => inner.Message == "physical cleanup failed");
        Assert.Equal(0, manager.PendingCount);
        Assert.Equal(1, manager.FailedCount);
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

    private sealed class DiagnosticRetiredGpuResource(string name, string state)
        : IRetiredGpuResource, IRetiredGpuResourceDiagnostics
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FullyDisposed => _completion.Task;

        public string DiagnosticName { get; } = name;

        public string DescribeState() => state;

        public bool TryFinalizePhysicalResources() => false;
    }

    private sealed class ThrowingRetiredGpuResource : IRetiredGpuResource
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FullyDisposed => _completion.Task;

        public bool TryFinalizePhysicalResources() =>
            throw new InvalidOperationException("physical cleanup failed");
    }
}
