namespace WTK.MediaForge.Core.Gpu;

/// <summary>
/// Stable identifier for a GPU texture shared across capture and render backends.
/// Used as the cache key for Vulkan external texture imports.
/// </summary>
public readonly record struct GpuTextureId(Guid Value)
{
    /// <summary>Creates a new unique texture identifier.</summary>
    public static GpuTextureId New() => new(Guid.NewGuid());
}
