namespace WTK.MediaForge.Core.Gpu.Resources;

internal readonly record struct GpuResourceId(Guid Value)
{
    public static GpuResourceId New() => new(Guid.NewGuid());

    public static GpuResourceId Empty => default;

    public bool IsEmpty => Value == Guid.Empty;
}
