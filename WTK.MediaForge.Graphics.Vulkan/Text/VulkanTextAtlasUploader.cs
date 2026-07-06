using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal sealed class VulkanTextAtlasUploader : IDisposable
{
    private readonly VulkanGpuResourcePool _pool;
    private readonly Dictionary<GpuTextureId, UploadedAtlas> _uploaded = [];
    private bool _disposed;

    public VulkanTextAtlasUploader(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _pool = new VulkanGpuResourcePool(device);
    }

    public UploadedAtlas Upload(FontAtlasAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var acquired = _pool.AcquireOffscreenTarget(
            new FrameSize((uint)asset.Width, (uint)asset.Height),
            GpuTextureUsage.Intermediate);

        var target = acquired.Target;
        target.CurrentLayout = ImageLayout.ShaderReadOnlyOptimal;

        var uploaded = new UploadedAtlas(acquired.Lease.TextureId, target.ImageView, acquired.Lease);
        _uploaded[uploaded.TextureId] = uploaded;
        return uploaded;
    }

    public bool TryGetImageView(GpuTextureId textureId, out ImageView imageView)
    {
        if (_uploaded.TryGetValue(textureId, out var uploaded))
        {
            imageView = uploaded.ImageView;
            return true;
        }

        imageView = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var uploaded in _uploaded.Values)
            uploaded.Lease.Dispose();

        _uploaded.Clear();
        _pool.Dispose();
    }
}

internal sealed class UploadedAtlas
{
    public UploadedAtlas(GpuTextureId textureId, ImageView imageView, GpuTextureLease lease)
    {
        TextureId = textureId;
        ImageView = imageView;
        Lease = lease;
    }

    public GpuTextureId TextureId { get; }

    public ImageView ImageView { get; }

    public GpuTextureLease Lease { get; }
}
