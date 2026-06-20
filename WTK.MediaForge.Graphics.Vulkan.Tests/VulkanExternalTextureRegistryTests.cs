using System.Diagnostics.CodeAnalysis;
using Vortice.DXGI;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class VulkanExternalTextureRegistryTests
{
    [Fact]
    public void VulkanExternalTextureRegistry_is_not_public()
    {
        Assert.False(typeof(VulkanExternalTextureRegistry).IsPublic);
        Assert.True(typeof(VulkanExternalTextureRegistry).IsNotPublic);
    }

    [Fact]
    public void Same_handle_reuses_same_vulkan_import()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            var first = context.Registry.Acquire(context.Handle);
            var second = context.Registry.Acquire(context.Handle);

            Assert.Same(first.Import, second.Import);
            Assert.Equal(1, context.Registry.EntryCount);

            first.Dispose();
            Assert.Equal(1, context.Registry.EntryCount);

            second.Dispose();
            Assert.Equal(1, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Different_handles_get_different_imports()
    {
        if (!TryCreateContext(out var context))
            return;

        if (!TryCreateSharedTexture(out var device, out var firstHandle))
            return;

        using (context)
        using (device)
        using (firstHandle)
        {
            using var secondHandle = D3D11SharedTextureFactory.CreateSharedTexture(
                device.Device,
                width: 64,
                height: 64);

            using var firstLease = context.Registry.Acquire(firstHandle);
            using var secondLease = context.Registry.Acquire(secondHandle);

            Assert.NotSame(firstLease.Import, secondLease.Import);
            Assert.Equal(2, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Retired_handle_import_destroyed_on_last_lease_release()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            var lease = context.Registry.Acquire(context.Handle);
            context.Handle.MarkRetired();

            lease.Dispose();

            Assert.Equal(0, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Acquire_retired_handle_before_first_lease_is_allowed()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            context.Handle.MarkRetired();

            using var lease = context.Registry.Acquire(context.Handle);
            lease.Dispose();

            Assert.Equal(0, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Acquire_retired_handle_with_live_lease_is_allowed()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            using var firstLease = context.Registry.Acquire(context.Handle);
            context.Handle.MarkRetired();

            using var secondLease = context.Registry.Acquire(context.Handle);

            Assert.Same(firstLease.Import, secondLease.Import);
        }
    }

    [Fact]
    public async Task Acquire_disposed_or_closed_handle_throws()
    {
        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (device)
        {
            using var deviceContext = VulkanHeadlessDevice.Create();
            var registry = new VulkanExternalTextureRegistry(deviceContext);

            handle.Dispose();

            Assert.Throws<ObjectDisposedException>(() => registry.Acquire(handle));

            await registry.DisposeAsync();
            deviceContext.Dispose();
        }
    }

    [Fact]
    public void Repeated_acquire_release_smoke_keeps_registry_entry_count_stable()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            for (var i = 0; i < 50; i++)
            {
                var lease = context.Registry.Acquire(context.Handle);
                lease.Dispose();
            }

            Assert.Equal(1, context.Registry.EntryCount);
            context.Registry.CollectUnused();
            Assert.Equal(1, context.Registry.EntryCount);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Stress)]
    public void Repeated_acquire_release_keeps_registry_entry_count_stable()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            for (var i = 0; i < 1000; i++)
            {
                var lease = context.Registry.Acquire(context.Handle);
                lease.Dispose();
            }

            Assert.Equal(1, context.Registry.EntryCount);
            context.Registry.CollectUnused();
            Assert.Equal(1, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Concurrent_acquire_same_handle_creates_single_import()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            const int threadCount = 8;
            var barrier = new Barrier(threadCount);
            var leases = new VulkanExternalTextureLease[threadCount];
            var exceptions = new Exception?[threadCount];

            var threads = Enumerable.Range(0, threadCount)
                .Select(index => new Thread(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        leases[index] = context!.Registry.Acquire(context.Handle);
                    }
                    catch (Exception ex)
                    {
                        exceptions[index] = ex;
                    }
                }))
                .ToArray();

            foreach (var thread in threads)
                thread.Start();

            foreach (var thread in threads)
                thread.Join();

            Assert.All(exceptions, ex => Assert.Null(ex));
            Assert.Equal(1, context.Registry.EntryCount);

            var firstImport = leases[0].Import;
            foreach (var lease in leases)
                Assert.Same(firstImport, lease.Import);

            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Fact]
    public void Acquire_does_not_hold_registry_lock_while_creating_import()
    {
        VulkanExternalTextureRegistry? registry = null;
        using var lockProbeCompleted = new ManualResetEventSlim();

        var factory = new DelegatingImportFactory((deviceContext, handle) =>
        {
            var probe = Task.Run(() =>
            {
                _ = registry!.EntryCount;
                lockProbeCompleted.Set();
            });

            if (!lockProbeCompleted.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Registry lock was held while creating a Vulkan import.");

            probe.GetAwaiter().GetResult();
            return VulkanD3D11TextureImport.Import(deviceContext, handle);
        });

        if (!TryCreateContext(out var context, factory))
            return;

        registry = context.Registry;

        using (context)
        using (var lease = context.Registry.Acquire(context.Handle))
        {
            Assert.True(lockProbeCompleted.IsSet);
        }
    }

    [Fact]
    public async Task Concurrent_acquire_different_textures_can_create_independently()
    {
        if (!TryCreateSharedTexture(out var device, out var firstHandle))
            return;

        using (device)
        using (firstHandle)
        using (var secondHandle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64))
        using (var bothImportsEntered = new CountdownEvent(2))
        {
            var factory = new DelegatingImportFactory((deviceContext, handle) =>
            {
                bothImportsEntered.Signal();

                if (!bothImportsEntered.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Different texture imports did not create independently.");

                return VulkanD3D11TextureImport.Import(deviceContext, handle);
            });

            VulkanHeadlessDevice deviceContext;
            try
            {
                deviceContext = VulkanHeadlessDevice.Create();
            }
            catch
            {
                return;
            }

            using var context = new RegistryTestContext(
                deviceContext,
                new VulkanExternalTextureRegistry(deviceContext, importFactory: factory),
                firstHandle);

            var firstTask = Task.Run(() => context.Registry.Acquire(firstHandle));
            var secondTask = Task.Run(() => context.Registry.Acquire(secondHandle));

            var leases = await Task.WhenAll(firstTask, secondTask);

            try
            {
                Assert.Equal(2, context.Registry.EntryCount);
                Assert.NotSame(leases[0].Import, leases[1].Import);
            }
            finally
            {
                foreach (var lease in leases)
                    lease.Dispose();
            }
        }
    }

    [Fact]
    public void Import_failure_does_not_leave_creating_entry_stuck()
    {
        var factory = new DelegatingImportFactory((_, _) =>
            throw new InvalidOperationException("controlled import failure"));

        if (!TryCreateContext(out var context, factory))
            return;

        using (context)
        {
            Assert.Throws<InvalidOperationException>(() => context.Registry.Acquire(context.Handle));
            Assert.Equal(0, context.Registry.EntryCount);
        }
    }

    [Fact]
    public void Acquire_after_import_failure_can_retry_cleanly()
    {
        var shouldFail = true;
        var factory = new DelegatingImportFactory((deviceContext, handle) =>
        {
            if (shouldFail)
            {
                shouldFail = false;
                throw new InvalidOperationException("controlled import failure");
            }

            return VulkanD3D11TextureImport.Import(deviceContext, handle);
        });

        if (!TryCreateContext(out var context, factory))
            return;

        using (context)
        {
            Assert.Throws<InvalidOperationException>(() => context.Registry.Acquire(context.Handle));
            Assert.Equal(0, context.Registry.EntryCount);

            using var lease = context.Registry.Acquire(context.Handle);

            Assert.Equal(2, factory.ImportCallCount);
            Assert.Equal(1, context.Registry.EntryCount);
        }
    }

    [Fact]
    public async Task DisposeAsync_during_import_creation_disposes_created_import()
    {
        VulkanD3D11TextureImport? createdImport = null;
        using var importCreated = new ManualResetEventSlim();
        using var allowImportReturn = new ManualResetEventSlim();

        var factory = new DelegatingImportFactory((deviceContext, handle) =>
        {
            createdImport = VulkanD3D11TextureImport.Import(deviceContext, handle);
            importCreated.Set();

            if (!allowImportReturn.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out waiting to return controlled import.");

            return createdImport;
        });

        if (!TryCreateContext(out var context, factory))
            return;

        try
        {
            var acquireTask = Task.Run(() => context.Registry.Acquire(context.Handle));

            Assert.True(importCreated.Wait(TimeSpan.FromSeconds(5)));

            await context.Registry.DisposeAsync();
            allowImportReturn.Set();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await acquireTask);

            Assert.NotNull(createdImport);
            Assert.True(createdImport.IsDisposed);
        }
        finally
        {
            allowImportReturn.Set();
            context.Dispose();
        }
    }

    [Fact]
    public async Task Registry_DisposeAsync_throws_if_refcount_active()
    {
        if (!TryCreateContext(out var context))
            return;

        var lease = context.Registry.Acquire(context.Handle);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Registry.DisposeAsync().AsTask());
        }
        finally
        {
            lease.Dispose();
            context.Dispose();
        }
    }

    [Fact]
    public void Registry_DisposeAsync_succeeds_after_lease_release()
    {
        if (!TryCreateContext(out var context))
            return;

        using (context)
        {
            var lease = context!.Registry.Acquire(context.Handle);
            lease.Dispose();
        }
    }

    private static bool TryCreateContext(
        [NotNullWhen(true)] out RegistryTestContext? context,
        IVulkanExternalTextureImportFactory? importFactory = null)
    {
        context = null;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return false;

        try
        {
            var deviceContext = VulkanHeadlessDevice.Create();
            context = new RegistryTestContext(
                deviceContext,
                new VulkanExternalTextureRegistry(deviceContext, importFactory: importFactory),
                handle);
            device.Dispose();
            return true;
        }
        catch
        {
            device.Dispose();
            handle.Dispose();
            return false;
        }
    }

    private static bool TryCreateSharedTexture(
        out D3D11GpuDevice device,
        out D3D11SharedTextureFrameHandle handle)
    {
        device = null!;
        handle = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RegistryTestContext : IDisposable
    {
        public RegistryTestContext(
            VulkanHeadlessDevice deviceContext,
            VulkanExternalTextureRegistry registry,
            D3D11SharedTextureFrameHandle handle)
        {
            DeviceContext = deviceContext;
            Registry = registry;
            Handle = handle;
        }

        public VulkanHeadlessDevice DeviceContext { get; }

        public VulkanExternalTextureRegistry Registry { get; }

        public D3D11SharedTextureFrameHandle Handle { get; }

        public void Dispose()
        {
            Registry.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Handle.Dispose();
            DeviceContext.Dispose();
        }
    }

    private sealed class DelegatingImportFactory : IVulkanExternalTextureImportFactory
    {
        private readonly Func<VulkanHeadlessDevice, D3D11SharedTextureFrameHandle, VulkanD3D11TextureImport> _import;
        private int _importCallCount;

        public DelegatingImportFactory(
            Func<VulkanHeadlessDevice, D3D11SharedTextureFrameHandle, VulkanD3D11TextureImport> import)
        {
            _import = import;
        }

        public int ImportCallCount => Volatile.Read(ref _importCallCount);

        public VulkanD3D11TextureImport Import(
            VulkanHeadlessDevice deviceContext,
            D3D11SharedTextureFrameHandle handle)
        {
            Interlocked.Increment(ref _importCallCount);
            return _import(deviceContext, handle);
        }
    }
}
