using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using RuntimeRenderOutputSink = WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.Offscreen ||
        typeId == RenderOutputTypes.PreviewWindow;

    public RuntimeRenderOutputSink CreateSink(RenderOutputTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.TypeId == RenderOutputTypes.Offscreen)
            return new OffscreenRenderOutputSink();

        if (target is WindowsHostedPreviewRenderOutputTarget preview)
            return new Win32HostedPreviewRenderOutputSink(preview.WindowHandle);

        if (target.TypeId == RenderOutputTypes.PreviewWindow)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"output.{target.TypeId.Value}",
                "Preview output requires a Windows hosted preview surface created by the platform adapter.");
        }

        throw new MediaForgeUnsupportedFeatureException(
            $"output.{target.TypeId.Value}",
            $"Output target '{target.TypeId.Value}' is not supported by the Windows facade.");
    }

    private sealed class OffscreenRenderOutputSink : RuntimeRenderOutputSink
    {
        public RenderOutputBindingSnapshot CreateBinding(
            RenderOutputId outputId,
            FrameSize surfaceSize,
            long bindingVersion) =>
            new()
            {
                OutputId = outputId,
                TargetKind = RenderTargetKind.Offscreen,
                NativeHandle = 0,
                SurfaceSize = surfaceSize,
                BindingVersion = bindingVersion
            };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Win32HostedPreviewRenderOutputSink(nint windowHandle) : RuntimeRenderOutputSink
    {
        public RenderOutputBindingSnapshot CreateBinding(
            RenderOutputId outputId,
            FrameSize surfaceSize,
            long bindingVersion) =>
            new()
            {
                OutputId = outputId,
                TargetKind = RenderTargetKind.Win32Hwnd,
                NativeHandle = windowHandle,
                SurfaceSize = surfaceSize,
                BindingVersion = bindingVersion
            };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
