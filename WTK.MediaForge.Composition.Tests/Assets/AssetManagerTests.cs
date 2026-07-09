using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Assets;

public sealed class AssetManagerTests
{
    [Fact]
    public void Fifty_layers_same_png_share_one_cached_texture()
    {
        var decoder = new CountingStaticImageDecoder();
        var manager = new AssetManager(decoder);
        StaticCpuAsset? shared = null;

        var handles = new List<RefCountedAssetHandle<StaticCpuAsset>>(capacity: 50);
        for (var index = 0; index < 50; index++)
            handles.Add(manager.LoadTexture("logo.png"));

        shared = handles[0].Value;
        Assert.All(handles, handle => Assert.Same(shared, handle.Value));
        Assert.Equal(1, decoder.DecodeCalls);
        Assert.Equal(1, manager.TextureCache.LiveEntryCount);

        foreach (var handle in handles)
            handle.Dispose();

        Assert.Equal(0, manager.TextureCache.LiveEntryCount);
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

    private sealed class CountingStaticImageDecoder : IStaticImageAssetDecoder
    {
        public int DecodeCalls { get; private set; }

        public StaticCpuAsset Decode(string path)
        {
            DecodeCalls++;
            return new StaticCpuAsset
            {
                Path = path,
                Size = new FrameSize(8, 8),
                PixelFormat = RenderPixelFormat.Rgba8Unorm,
                Pixels = new byte[8 * 8 * 4],
                TransportKind = MediaTransportKind.StaticCpuAsset
            };
        }
    }
}
