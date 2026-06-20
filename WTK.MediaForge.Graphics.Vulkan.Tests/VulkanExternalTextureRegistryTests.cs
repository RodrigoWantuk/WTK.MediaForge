using Vortice.DXGI;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public class VulkanExternalTextureRegistryTests
{
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
    public void Acquire_disposed_or_closed_handle_throws()
    {
        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (device)
        {
            using var deviceContext = VulkanHeadlessDevice.Create();
            var registry = new VulkanExternalTextureRegistry(deviceContext);

            handle.Dispose();

            Assert.Throws<ObjectDisposedException>(() => registry.Acquire(handle));

            registry.DisposeAsync().AsTask().GetAwaiter().GetResult();
            deviceContext.Dispose();
        }
    }

    [Fact]
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

    private static bool TryCreateContext(out RegistryTestContext? context)
    {
        context = null;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return false;

        try
        {
            var deviceContext = VulkanHeadlessDevice.Create();
            context = new RegistryTestContext(deviceContext, new VulkanExternalTextureRegistry(deviceContext), handle);
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
}
