using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanD3D11TextureImport : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly nint _importedSharedHandle;
    private Image _image;
    private DeviceMemory _memory;
    private bool _disposed;

    private VulkanD3D11TextureImport(
        Vk vk,
        Device device,
        Image image,
        DeviceMemory memory,
        nint importedSharedHandle,
        D3D11SharedTextureFrameHandle sourceHandle)
    {
        _vk = vk;
        _device = device;
        _image = image;
        _memory = memory;
        _importedSharedHandle = importedSharedHandle;
        SourceHandle = sourceHandle;
        Width = sourceHandle.TextureSize.Width;
        Height = sourceHandle.TextureSize.Height;
    }

    public D3D11SharedTextureFrameHandle SourceHandle { get; }

    public Image Image => _image;

    public DeviceMemory Memory => _memory;

    public uint Width { get; }

    public uint Height { get; }

    public static VulkanD3D11TextureImport Import(
        VulkanHeadlessDevice deviceContext,
        D3D11SharedTextureFrameHandle handle)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.HasSharedHandle)
            throw new InvalidOperationException("D3D11 shared texture handle is missing a shared NT handle.");

        var duplicatedHandle = Win32NativeHandle.DuplicateSharedHandle(handle.SharedHandle);
        var format = D3D11VulkanFormatMap.MapOrThrow(handle.Format);

        try
        {
            return Import(
                deviceContext.Vk,
                deviceContext.Device,
                deviceContext.FindMemoryType,
                duplicatedHandle,
                handle,
                format);
        }
        catch
        {
            Win32NativeHandle.CloseSharedHandle(duplicatedHandle);
            throw;
        }
    }

    private static VulkanD3D11TextureImport Import(
        Vk vk,
        Device device,
        Func<uint, MemoryPropertyFlags, uint> findMemoryType,
        nint importedSharedHandle,
        D3D11SharedTextureFrameHandle sourceHandle,
        Format format)
    {
        var externalMemoryImageCreateInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = ExternalMemoryHandleTypeFlags.D3D11TextureBit
        };

        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalMemoryImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(sourceHandle.TextureSize.Width, sourceHandle.TextureSize.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        if (vk.CreateImage(device, &imageCreateInfo, null, out Image image) != Result.Success)
            throw new InvalidOperationException("Create imported Vulkan image failed.");

        vk.GetImageMemoryRequirements(device, image, out MemoryRequirements memoryRequirements);

        var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
        {
            SType = StructureType.ImportMemoryWin32HandleInfoKhr,
            HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
            Handle = importedSharedHandle
        };

        var dedicatedAllocateInfo = new MemoryDedicatedAllocateInfo
        {
            SType = StructureType.MemoryDedicatedAllocateInfo,
            PNext = &importMemoryInfo,
            Image = image,
            Buffer = default
        };

        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = &dedicatedAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = findMemoryType(
                memoryRequirements.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit)
        };

        if (vk.AllocateMemory(device, &allocateInfo, null, out DeviceMemory memory) != Result.Success)
        {
            vk.DestroyImage(device, image, null);
            throw new InvalidOperationException("Allocate imported Vulkan memory failed.");
        }

        if (vk.BindImageMemory(device, image, memory, 0) != Result.Success)
        {
            vk.FreeMemory(device, memory, null);
            vk.DestroyImage(device, image, null);
            throw new InvalidOperationException("Bind imported Vulkan image memory failed.");
        }

        return new VulkanD3D11TextureImport(vk, device, image, memory, importedSharedHandle, sourceHandle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_image.Handle != 0)
        {
            _vk.DestroyImage(_device, _image, null);
            _image = default;
        }

        if (_memory.Handle != 0)
        {
            _vk.FreeMemory(_device, _memory, null);
            _memory = default;
        }

        if (_importedSharedHandle != 0)
            Win32NativeHandle.CloseSharedHandle(_importedSharedHandle);
    }
}
