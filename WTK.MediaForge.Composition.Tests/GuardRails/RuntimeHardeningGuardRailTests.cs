using System.Text.RegularExpressions;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.GuardRails;

public sealed class RuntimeHardeningGuardRailTests
{
    private static readonly Regex[] UnboundedSyncWaitPatterns =
    [
        new(@"\.Wait\s*\(\s*\)", RegexOptions.CultureInvariant),
        new(@"\bTask\.WaitAll\s*\(", RegexOptions.CultureInvariant),
        new(@"\bTask\.WaitAny\s*\(", RegexOptions.CultureInvariant),
        new(@"\bAsTask\s*\(\s*\)\s*\.GetAwaiter\s*\(\s*\)\s*\.GetResult\s*\(\s*\)", RegexOptions.CultureInvariant),
        new(@"\bThread\.Sleep\s*\(", RegexOptions.CultureInvariant)
    ];

    private static readonly Regex LifetimeTodoPattern =
        new(@"\b(TODO|FIXME|HACK)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SimulateApiPattern =
        new(@"\bSimulate[A-Za-z0-9_]*\b", RegexOptions.CultureInvariant);

    private static readonly Regex DangerousInteropPattern =
        new(@"\bDangerousGetHandleForInterop\b", RegexOptions.CultureInvariant);

    private static readonly Regex NativeHandleCollectionPattern =
        new(@"\b(?:ConcurrentDictionary|Dictionary|HashSet)\s*<\s*(?:nint|nuint|IntPtr)\b", RegexOptions.CultureInvariant);

    private static readonly string[] ProductProjectDirectories =
    [
        "WTK.MediaForge.Capture",
        "WTK.MediaForge.Composition",
        "WTK.MediaForge.Core",
        "WTK.MediaForge.Diagnostics",
        "WTK.MediaForge.Graphics.D3D11",
        "WTK.MediaForge.Graphics.Vulkan",
        "WTK.MediaForge.Windows"
    ];

    private static readonly AllowlistEntry[] UnboundedSyncWaitAllowlist =
    [
        new(
            "WTK.MediaForge.Composition/Runtime/Rendering/SlowNullRenderBackend.cs",
            "Thread.Sleep(",
            "SlowNullRenderBackend is an explicit test/diagnostic render delay backend; production renderers remain async/bounded.")
    ];

    private static readonly AllowlistEntry[] DangerousInteropAllowlist =
    [
        new(
            "WTK.MediaForge.Graphics.D3D11/SharedWin32Handle.cs",
            "DangerousGetHandleForInterop",
            "SharedWin32Handle is the narrow D3D11 interop wrapper around native shared handles."),
        new(
            "WTK.MediaForge.Graphics.Vulkan/Rendering/VulkanD3D11TextureImport.cs",
            "DangerousGetHandleForInterop",
            "The Vulkan import bridge consumes a duplicated D3D11 shared handle for real GPU interop."),
        new(
            "WTK.MediaForge.Graphics.Vulkan/Rendering/VulkanD3D11ExportBlit.cs",
            "DangerousGetHandleForInterop",
            "The Vulkan export proof imports a duplicated D3D11 shared handle for real GPU interop."),
        new(
            "WTK.MediaForge.Composition/Runtime/Rendering/PreviewPanelClientSizeTracker.cs",
            "ConcurrentDictionary<nint",
            "Preview panel sizing is keyed by Win32 panel handle, not by texture or GPU frame identity.")
    ];

    [Fact]
    public void Product_runtime_code_does_not_add_unbounded_sync_waits()
    {
        var violations = ScanProductSourceLines((relativePath, line) =>
        {
            if (!UnboundedSyncWaitPatterns.Any(pattern => pattern.IsMatch(line)))
                return null;

            if (UnboundedSyncWaitAllowlist.Any(entry => entry.Matches(relativePath, line)))
                return null;

            return "Unbounded synchronous wait found. Use async flow or an explicit timeout with observable failure.";
        });

        Assert.Empty(violations);
    }

    [Fact]
    public void Product_runtime_code_does_not_add_lifetime_todos()
    {
        var violations = ScanProductSourceLines((_, line) =>
            LifetimeTodoPattern.IsMatch(line)
                ? "TODO/FIXME/HACK found in product runtime. Lifetime and shutdown paths must be implemented or explicitly tracked in docs."
                : null);

        Assert.Empty(violations);
    }

    [Fact]
    public void Product_runtime_code_does_not_expose_simulate_apis()
    {
        var violations = ScanProductSourceLines((_, line) =>
            SimulateApiPattern.IsMatch(line)
                ? "Simulate* API found in product runtime. Use test-only fault injectors instead."
                : null);

        Assert.Empty(violations);
    }

    [Fact]
    public void Native_handle_usage_stays_in_explicit_interop_allowlist()
    {
        var violations = ScanProductSourceLines((relativePath, line) =>
        {
            if (!DangerousInteropPattern.IsMatch(line) && !NativeHandleCollectionPattern.IsMatch(line))
                return null;

            if (DangerousInteropAllowlist.Any(entry => entry.Matches(relativePath, line)))
                return null;

            return "Native handle usage is not allowlisted. Handles must not become logical texture/frame identity.";
        });

        Assert.Empty(violations);
    }

    private static IReadOnlyList<string> ScanProductSourceLines(
        Func<string, string, string?> inspectLine)
    {
        var repoRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateProductSourceFiles(repoRoot))
        {
            var relativePath = ToRepositoryRelativePath(repoRoot, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var message = inspectLine(relativePath, lines[i]);
                if (message is null)
                    continue;

                violations.Add($"{relativePath}:{i + 1}: {message}");
            }
        }

        return violations;
    }

    private static IEnumerable<string> EnumerateProductSourceFiles(string repoRoot)
    {
        foreach (var directoryName in ProductProjectDirectories)
        {
            var directory = Path.Combine(repoRoot, directoryName);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGeneratedOrBuildOutput(file))
                    continue;

                yield return file;
            }
        }
    }

    private static bool IsGeneratedOrBuildOutput(string file)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var parts = file.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WTK.MediaForge.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private static string ToRepositoryRelativePath(string repoRoot, string file)
    {
        return Path.GetRelativePath(repoRoot, file)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private sealed record AllowlistEntry(string RelativePath, string Token, string Reason)
    {
        public bool Matches(string relativePath, string line) =>
            relativePath.Equals(RelativePath, StringComparison.OrdinalIgnoreCase) &&
            line.Contains(Token, StringComparison.Ordinal);
    }
}
