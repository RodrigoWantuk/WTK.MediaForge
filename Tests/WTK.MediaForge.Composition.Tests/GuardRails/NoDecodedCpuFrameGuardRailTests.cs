using Xunit;

namespace WTK.MediaForge.Composition.Tests.GuardRails;

public sealed class NoDecodedCpuFrameGuardRailTests
{
    [Fact]
    public void No_DecodedCpuFrame_type_in_solution()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}.cursor{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}GuardRails{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("DecodedCpuFrame", StringComparison.Ordinal))
                violations.Add(file);
        }

        Assert.Empty(violations);
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

        throw new DirectoryNotFoundException("Could not locate WTK.MediaForge.sln from the test output directory.");
    }
}
