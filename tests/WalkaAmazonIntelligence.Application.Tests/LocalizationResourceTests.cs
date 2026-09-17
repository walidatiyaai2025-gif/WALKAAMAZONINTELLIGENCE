using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace WalkaAmazonIntelligence.Application.Tests;

public class LocalizationResourceTests
{
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PROJECT_PLAN.md")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Descendants()
            .Attributes(x + "Key")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EnglishAndArabicResourceKeysAreInExactParity()
    {
        var root = RepositoryRoot();
        var english = ResourceKeys(Path.Combine(root, "src", "WalkaAmazonIntelligence.Desktop", "Resources", "Strings.en.xaml"));
        var arabic = ResourceKeys(Path.Combine(root, "src", "WalkaAmazonIntelligence.Desktop", "Resources", "Strings.ar.xaml"));

        Assert.NotEmpty(english);
        Assert.Equal(english.Order().ToArray(), arabic.Order().ToArray());
    }

    [Fact]
    public void ShellLocalizedTextKeysExistInBothLanguagesAndCultureDirectionAreBound()
    {
        var root = RepositoryRoot();
        var desktop = Path.Combine(root, "src", "WalkaAmazonIntelligence.Desktop");
        var shell = File.ReadAllText(Path.Combine(desktop, "MainWindow.xaml"));
        var english = ResourceKeys(Path.Combine(desktop, "Resources", "Strings.en.xaml"));
        var arabic = ResourceKeys(Path.Combine(desktop, "Resources", "Strings.ar.xaml"));

        var keys = Regex.Matches(shell, @"(?:Text|Content|Header)=""\{DynamicResource\s+([^}]+)\}""")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            Assert.Contains(key, english);
            Assert.Contains(key, arabic);
        });

        Assert.Contains("FlowDirection=\"{Binding Direction}\"", shell);
        Assert.Contains("Language=\"{Binding UiLanguage}\"", shell);
        Assert.DoesNotContain("Storage is configurable through WALKA_Storage__Root", shell);
        Assert.DoesNotContain("Read-only development build. Seller Sales", shell);
    }
}
