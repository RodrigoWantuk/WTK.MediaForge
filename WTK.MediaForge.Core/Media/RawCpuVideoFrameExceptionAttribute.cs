namespace WTK.MediaForge.Core.Media;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class RawCpuVideoFrameExceptionAttribute : Attribute
{
    public RawCpuVideoFrameExceptionAttribute(RawCpuVideoFrameExceptionKind kind) => Kind = kind;

    public RawCpuVideoFrameExceptionKind Kind { get; }
}
