using System.Runtime.Versioning;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsImageSourceProviderFactoryTests
{
    [Fact]
    [Trait("Category", "GPU")]
    public async Task Image_provider_uploads_png_once_and_returns_d3d11_gpu_frame()
    {
        if (!OperatingSystem.IsWindows() || !TryCreateD3D11Device())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"mf-image-source-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));

        var factory = new WindowsImageSourceProviderFactory();
        var provider = factory.CreateProvider(new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = "Logo",
            TypeId = MediaSourceTypes.ImageFile,
            Settings = MediaSourceSettingsSerializer.ToJson(new ImageFileSourceSettings
            {
                Path = path
            })
        });

        try
        {
            await provider.StartAsync(CancellationToken.None);

            Assert.Equal(MediaSourceState.Running, provider.State);
            Assert.True(provider.TryAcquireLatestFrame(out var lease));

            using (lease)
            {
                Assert.Equal(GpuFrameBackend.D3D11SharedTexture, lease.Frame.Backend);
                var handle = Assert.IsType<D3D11SharedTextureFrameHandle>(lease.Frame.Handle);
                Assert.Equal(new FrameSize(1, 1), handle.TextureSize);
                Assert.Equal(D3D11SharedTextureSyncKeys.Consumer, handle.ProducerAcquireKey);
            }

            await provider.StopAsync(CancellationToken.None);
            Assert.Equal(MediaSourceState.Stopped, provider.State);
        }
        finally
        {
            if (provider is IDisposable disposable)
                disposable.Dispose();

            File.Delete(path);
        }
    }

    [Fact]
    public void Image_provider_rejects_webp_until_product_path_is_approved()
    {
        var factory = new WindowsImageSourceProviderFactory();

        var ex = Assert.Throws<NotSupportedException>(() =>
            factory.CreateProvider(new MediaForgeSourceDefinition
            {
                Id = SourceId.New(),
                Name = "Logo",
                TypeId = MediaSourceTypes.ImageFile,
                Settings = MediaSourceSettingsSerializer.ToJson(new ImageFileSourceSettings
                {
                    Path = "logo.webp"
                })
            }));

        Assert.Contains("WebP is Planned", ex.Message, StringComparison.Ordinal);
    }

    private static bool TryCreateD3D11Device()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out var adapter).Failure || adapter is null)
                return false;

            using var device = D3D11GpuDevice.CreateForAdapter(adapter);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
