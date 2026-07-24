namespace WTK.MediaForge.Composition.Tests.GuardRails;

public static class RawCpuFrameAllowlist
{
    public static readonly IReadOnlyList<string> AllowedNamespacePrefixes =
    [
        "WTK.MediaForge.Composition.Tests",
        "WTK.MediaForge.Core.Tests",
        "WTK.MediaForge.Diagnostics.Tests",
        "WTK.MediaForge.Graphics.D3D11.Tests",
        "WTK.MediaForge.Graphics.Vulkan.Tests",
        "WTK.MediaForge.Capture.Tests",
        "WTK.MediaForge.Windows.Tests",
        "WTK.MediaForge.Studio.Tests",
        "WTK.MediaForge.Diagnostics",
        "WTK.MediaForge.Sample"
    ];

    public static readonly IReadOnlyList<string> AllowedTypeNameSuffixes =
    [
        "Tests",
        "TestDoubles",
        "ManualScreenshotService"
    ];

    public static bool IsNamespaceAllowed(string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return false;

        return AllowedNamespacePrefixes.Any(prefix =>
            namespaceName.StartsWith(prefix, StringComparison.Ordinal));
    }
}
