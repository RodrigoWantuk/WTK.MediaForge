using System.Text.RegularExpressions;

namespace WTK.MediaForge.Composition.Tests.GuardRails;

public static class RawCpuFrameGuardRailScanner
{
    private static readonly (Regex Pattern, string Description)[] ForbiddenPatterns =
    [
        (new Regex(@"\bCpuReadbackSink\b", RegexOptions.CultureInvariant), "CpuReadbackSink reference"),
        (new Regex(@"\bWriteableBitmap\b", RegexOptions.CultureInvariant), "WriteableBitmap reference"),
        (new Regex(@"\bSystem\.Drawing\.Bitmap\b", RegexOptions.CultureInvariant), "System.Drawing.Bitmap reference"),
        (new Regex(@"\brawvideo\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), "rawvideo pipe reference"),
        (new Regex(@"\blibx264\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), "libx264 reference"),
        (new Regex(@"\bFFmpeg\b", RegexOptions.CultureInvariant), "FFmpeg reference")
    ];

    public static IReadOnlyList<string> ScanSource(string source, string? namespaceName = null)
    {
        if (RawCpuFrameAllowlist.IsNamespaceAllowed(namespaceName))
            return Array.Empty<string>();

        var violations = new List<string>();
        foreach (var (pattern, description) in ForbiddenPatterns)
        {
            if (pattern.IsMatch(source))
                violations.Add($"{description} in namespace '{namespaceName ?? "unknown"}'.");
        }

        return violations;
    }

    public static IReadOnlyList<string> ScanAssemblyTypes(Type[] types)
    {
        var violations = new List<string>();
        foreach (var type in types)
        {
            if (RawCpuFrameAllowlist.IsNamespaceAllowed(type.Namespace))
                continue;

            if (RawCpuFrameAllowlist.AllowedTypeNameSuffixes.Any(s => type.Name.EndsWith(s, StringComparison.Ordinal)))
                continue;

            if (type.Name.Contains("CpuReadbackSink", StringComparison.Ordinal)
                && type.Namespace?.Contains(".Outputs", StringComparison.Ordinal) == true)
            {
                violations.Add(
                    $"Type {type.FullName} references CpuReadbackSink outside allowlist; move to debug/test namespace or register exception.");
            }
        }

        return violations;
    }
}
