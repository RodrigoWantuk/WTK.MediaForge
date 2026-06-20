using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanD3D11TextureImport : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Image _image;
    private DeviceMemory _memory;
    private bool _disposed;

    private VulkanD3D11TextureImport(
        Vk vk,
        Device device,
        Image image,
        DeviceMemory memory,
        nint sharedHandle,
        uint width,
        uint height)
    {
        _vk = vk;
        _device = device;
        _image = image;
        _memory = memory;
        SharedHandle = sharedHandle;
        Width = width;
        Height = height;
    }

    public Image Image => _image;

    public DeviceMemory Memory => _memory;

    public nint SharedHandle { get; }

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

        return Import(
            deviceContext.Vk,
            deviceContext.Device,
            deviceContext.PhysicalDevice,
            deviceContext.FindMemoryType,
            handle.SharedHandle,
            handle.TextureSize.Width,
            handle.TextureSize.Height);
    }

    public static VulkanD3D11TextureImport Import(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        Func<uint, MemoryPropertyFlags, uint> findMemoryType,
        nint sharedHandle,
        uint width,
        uint height)
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
            Format = Format.B8G8R8A8Unorm,
            Extent = new Extent3D(width, height, 1),
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
            Handle = sharedHandle
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

        _ = physicalDevice;

        return new VulkanD3D11TextureImport(vk, device, image, memory, sharedHandle, width, height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_memory.Handle != 0)
        {
            _vk.FreeMemory(_device, _memory, null);
            _memory = default;
        }

        if (_image.Handle != 0)
        {
            _vk.DestroyImage(_device, _image, null);
            _image = default;
        }
    }
}
