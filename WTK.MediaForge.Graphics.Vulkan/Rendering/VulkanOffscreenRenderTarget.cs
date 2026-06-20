using Silk.NET.Vulkan;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanOffscreenRenderTarget : IVulkanOffscreenRenderTarget
{
    private readonly VulkanHeadlessDevice _deviceContext;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private int _disposed;

    public VulkanOffscreenRenderTarget(VulkanHeadlessDevice deviceContext, FrameSize size)
    {
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));

        if (size.Width == 0 || size.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Offscreen target dimensions must be greater than zero.");

        Size = size;
        Interlocked.Increment(ref VulkanOffscreenRenderTargetLifetime.LiveCount);
        var resources = CreateResources(size.Width, size.Height);
        ApplyResources(resources);
    }

    public FrameSize Size { get; private set; }

    public Image Image => _image;

    public ImageView ImageView => _imageView;

    internal VulkanHeadlessDevice DeviceContext => _deviceContext;

    public ImageLayout CurrentLayout { get; set; } = ImageLayout.Undefined;

    public void Resize(FrameSize newSize)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (newSize.Width == 0 || newSize.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(newSize), "Offscreen target dimensions must be greater than zero.");

        if (newSize == Size)
            return;

        var resources = CreateResources(newSize.Width, newSize.Height);

        DestroyResources();
        Size = newSize;
        ApplyResources(resources);
        CurrentLayout = ImageLayout.Undefined;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        DestroyResources();
        Interlocked.Decrement(ref VulkanOffscreenRenderTargetLifetime.LiveCount);
        Interlocked.Increment(ref VulkanOffscreenRenderTargetLifetime.DisposeCount);
    }

    private CreatedResources CreateResources(uint width, uint height)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;
        Image image = default;
        DeviceMemory memory = default;
        ImageView imageView = default;

        try
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                Extent = new Extent3D(width, height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            if (vk.CreateImage(device, &imageInfo, null, out image) != Result.Success)
                throw new InvalidOperationException("vkCreateImage failed for offscreen render target.");

            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements requirements);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _deviceContext.FindMemoryType(
                    requirements.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit)
            };

            if (vk.AllocateMemory(device, &allocInfo, null, out memory) != Result.Success)
                throw new InvalidOperationException("vkAllocateMemory failed for offscreen render target.");

            if (vk.BindImageMemory(device, image, memory, 0) != Result.Success)
                throw new InvalidOperationException("vkBindImageMemory failed for offscreen render target.");

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (vk.CreateImageView(device, &viewInfo, null, out imageView) != Result.Success)
                throw new InvalidOperationException("vkCreateImageView failed for offscreen render target.");

            return new CreatedResources(image, memory, imageView);
        }
        catch
        {
            DestroyResources(image, memory, imageView);
            throw;
        }
    }

    private void ApplyResources(CreatedResources resources)
    {
        _image = resources.Image;
        _memory = resources.Memory;
        _imageView = resources.ImageView;
    }

    private void DestroyResources()
    {
        DestroyResources(_image, _memory, _imageView);
        _image = default;
        _memory = default;
        _imageView = default;
    }

    private void DestroyResources(Image image, DeviceMemory memory, ImageView imageView)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;

        if (imageView.Handle != 0)
        {
            vk.DestroyImageView(device, imageView, null);
        }

        if (image.Handle != 0)
        {
            vk.DestroyImage(device, image, null);
        }

        if (memory.Handle != 0)
        {
            vk.FreeMemory(device, memory, null);
        }
    }

    private readonly record struct CreatedResources(
        Image Image,
        DeviceMemory Memory,
        ImageView ImageView);
}
