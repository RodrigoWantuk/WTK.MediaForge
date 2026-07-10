using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Core.Gpu.Resources;

internal interface IGpuFrameHandleProvider
{
    IGpuFrameHandle FrameHandle { get; }
}
