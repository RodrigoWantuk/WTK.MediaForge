using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) => typeId == RenderOutputTypes.Offscreen;

    public IRenderOutputSink CreateSink(RenderOutputTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.TypeId != RenderOutputTypes.Offscreen)
            throw new NotSupportedException($"Output target '{target.TypeId.Value}' is not supported by the Windows facade yet.");

        return new OffscreenRenderOutputSink();
    }

    private sealed class OffscreenRenderOutputSink : IRenderOutputSink
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
}
