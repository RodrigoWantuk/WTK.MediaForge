using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsNdiPackagingTests
{
    [Fact]
    public void Windows_project_packs_licensed_ndi_runtime_assets_when_present()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "WTK.MediaForge.Windows",
            "WTK.MediaForge.Windows.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("MediaForgeNdiWindowsRuntimeRoot", project, StringComparison.Ordinal);
        Assert.Contains("Processing.NDI.Lib.x64.dll", project, StringComparison.Ordinal);
        Assert.Contains("runtimes\\win-x64\\native\\Processing.NDI.Lib.x64.dll", project, StringComparison.Ordinal);
        Assert.Contains("Processing.NDI.Lib.x86.dll", project, StringComparison.Ordinal);
        Assert.Contains("runtimes\\win-x86\\native\\Processing.NDI.Lib.x86.dll", project, StringComparison.Ordinal);
        Assert.Contains("Pack=\"true\"", project, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "WTK.MediaForge.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
