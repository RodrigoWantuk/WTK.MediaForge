using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class LocalizationCoverageTests
{
    private static readonly Regex VisibleTextProperty = new(
        "\\b(Text|Content|Header|PlaceholderText)=\"(?<value>[^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly string[] AllowedHardcodedFragments =
    {
        "{Binding",
        "{loc:",
        "{x:",
        "{}",
        "WTK MediaForge Studio",
        "X",
        "Y",
        "#",
        "FPS",
        "RTMP/IP",
        "RTSP/IP",
        "H.264",
        "MP4",
        "NDI",
        "Webcam",
        "Logo.png",
        "Inter",
        "Source",
        "Text",
        "Image",
        "Solid",
        "Fit",
        "100%",
        "+",
        "-",
        "□",
        "×",
        "●",
        "○",
        "▸",
        "▾",
        "↗",
        "↙"
    };

    [Fact]
    public void Resx_files_have_same_keys()
    {
        var baseKeys = LoadKeys("WTK.MediaForge.Studio/Resources/Strings.resx");
        var ptBrKeys = LoadKeys("WTK.MediaForge.Studio/Resources/Strings.pt-BR.resx");
        var enUsKeys = LoadKeys("WTK.MediaForge.Studio/Resources/Strings.en-US.resx");

        Assert.Empty(baseKeys.Except(ptBrKeys));
        Assert.Empty(baseKeys.Except(enUsKeys));
        Assert.Empty(ptBrKeys.Except(baseKeys));
        Assert.Empty(enUsKeys.Except(baseKeys));
    }

    [Fact]
    public void Primary_ui_does_not_expose_legacy_engine_or_mojibake_terms()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "WTK.MediaForge.Studio"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "WTK.MediaForge.Studio"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var text = string.Join("\n", files.Select(File.ReadAllText));

        Assert.DoesNotContain("Start Engine", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU idle", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preview idle", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Configuracoes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Transicao", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Producao", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Previa", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Saida", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Hardcoded_visible_xaml_text_is_documented_and_limited()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "WTK.MediaForge.Studio"), "*.axaml", SearchOption.AllDirectories);
        var unexpected = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in VisibleTextProperty.Matches(text))
            {
                var value = match.Groups["value"].Value;
                if (AllowedHardcodedFragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (value.Any(char.IsLetter))
                {
                    unexpected.Add($"{Path.GetRelativePath(root, file)}: {value}");
                }
            }
        }

        Assert.True(unexpected.Count <= 120, "Hardcoded visible strings must keep shrinking. Unexpected:\n" + string.Join("\n", unexpected.Take(40)));
    }

    private static ISet<string> LoadKeys(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => element.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "WTK.MediaForge.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
