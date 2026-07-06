using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Sources;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Assets;

[SupportedOSPlatform("windows")]
public sealed class AssetManagerTests
{
    [Fact]
    public void Fifty_layers_same_png_share_one_cached_texture()
    {
        var path = CreateTempPng();
        try
        {
            var manager = new AssetManager();
            StaticCpuAsset? shared = null;

            var handles = new List<RefCountedAssetHandle<StaticCpuAsset>>(capacity: 50);
            for (var index = 0; index < 50; index++)
                handles.Add(manager.LoadTexture(path));

            shared = handles[0].Value;
            Assert.All(handles, handle => Assert.Same(shared, handle.Value));
            Assert.Equal(1, manager.TextureCache.LiveEntryCount);

            foreach (var handle in handles)
                handle.Dispose();

            Assert.Equal(0, manager.TextureCache.LiveEntryCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Shader_cache_reuses_identical_source_key()
    {
        var manager = new AssetManager();
        var factoryCalls = 0;

        byte[] Factory()
        {
            factoryCalls++;
            return [1, 2, 3];
        }

        using var first = manager.LoadShader("mf_source_layer.frag", Factory);
        using var second = manager.LoadShader("mf_source_layer.frag", Factory);

        Assert.Equal(1, factoryCalls);
        Assert.Same(first.Value, second.Value);
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mf-asset-{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(8, 8);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Red);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
