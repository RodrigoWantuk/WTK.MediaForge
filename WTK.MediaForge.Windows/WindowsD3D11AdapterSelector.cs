using Vortice.DXGI;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows;

internal static class WindowsD3D11AdapterSelector
{
    public static D3D11GpuDevice CreateDevice(
        GpuAdapterAffinityState? adapterAffinity,
        bool requireVideoSupport = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("D3D11 adapters require Windows.");

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapter = adapterAffinity is null
            ? GetAdapterByIndex(factory, 0)
            : GetRequiredRendererAdapter(factory, adapterAffinity.Snapshot);

        try
        {
            return D3D11GpuDevice.CreateForAdapter(adapter, requireVideoSupport);
        }
        catch
        {
            adapter.Dispose();
            throw;
        }
    }

    private static IDXGIAdapter1 GetRequiredRendererAdapter(
        IDXGIFactory1 factory,
        GpuAdapterAffinitySnapshot affinity)
    {
        if (!affinity.IsAvailable)
        {
            throw new MediaForgeUnsupportedFeatureException(
                "gpu.adapter_affinity",
                "The Vulkan renderer did not publish a valid Windows adapter LUID. D3D11 interop cannot select adapter 0 implicitly.");
        }

        for (uint index = 0; ; index++)
        {
            var result = factory.EnumAdapters1(index, out var adapter);
            if (result.Failure)
                break;

            var luid = adapter.Description1.Luid;
            if (luid.LowPart == affinity.AdapterLuid.LowPart &&
                luid.HighPart == affinity.AdapterLuid.HighPart)
            {
                return adapter;
            }

            adapter.Dispose();
        }

        throw new MediaForgeUnsupportedFeatureException(
            "gpu.adapter_affinity",
            $"The Vulkan adapter '{affinity.DeviceName}' ({affinity.AdapterLuid}) is unavailable to D3D11. Cross-GPU media interop is prohibited.");
    }

    private static IDXGIAdapter1 GetAdapterByIndex(IDXGIFactory1 factory, uint index)
    {
        factory.EnumAdapters1(index, out var adapter).CheckError();
        return adapter;
    }
}
