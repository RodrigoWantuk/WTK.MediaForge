using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Windows;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public sealed class WindowsHostedPreviewSurfaceTests
{
    [Fact]
    public void MediaForgeWindows_creates_hosted_preview_surface_without_public_native_handle()
    {
        var surface = MediaForgeWindows.CreateHostedPreviewSurface();

        Assert.False(surface.Id.IsEmpty);
        Assert.Equal(HostedPreviewSurfaceState.Detached, surface.State);
        Assert.DoesNotContain(
            typeof(WindowsHostedPreviewSurface).GetConstructors(),
            constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(nint)));
    }

    [Fact]
    public async Task Windows_factory_creates_preview_binding_from_adapter_bound_hosted_surface()
    {
        var surface = new WindowsHostedPreviewSurface(windowHandle: 123);
        var target = surface.CreateRenderOutputTarget();
        var factory = new WindowsRenderOutputSinkFactory();

        await using var sink = factory.CreateSink(target);
        var binding = sink.CreateBinding(RenderOutputId.New(), new FrameSize(640, 360), bindingVersion: 7);

        Assert.Equal(RenderTargetKind.Win32Hwnd, binding.TargetKind);
        Assert.Equal(123, binding.NativeHandle);
        Assert.Equal(7, binding.BindingVersion);
    }

    [Fact]
    public void Windows_factory_rejects_legacy_public_winforms_target_for_product_preview()
    {
        var factory = new WindowsRenderOutputSinkFactory();

        var exception = Assert.Throws<MediaForgeUnsupportedFeatureException>(() =>
            factory.CreateSink(new WinFormsPreviewRenderOutputTarget(123)));

        Assert.Contains("hosted preview surface", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
