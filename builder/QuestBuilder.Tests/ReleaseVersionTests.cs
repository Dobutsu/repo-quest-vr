using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;

namespace RepoQuest.Builder.Tests;

public sealed class ReleaseVersionTests
{
    private static string SourceFile([CallerFilePath] string path = "") => path;

    [Fact]
    public void DevelopmentDefaultsFollowUpstreamReleaseConventions()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", ".."));
        using var kit = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "builder", "kit.json")));
        var expected = kit.RootElement.GetProperty("version").GetString();
        Assert.Equal("dev", expected);
        foreach (var file in new[] { "builder/Directory.Build.props", "tools/CloudHost.Pc/CloudHost.Pc.csproj" })
        {
            var project = XDocument.Load(Path.Combine(root, file));
            Assert.Equal("0.0.0", Assert.Single(project.Descendants("Version")).Value);
        }
        Assert.Contains("[Parameter(Mandatory = $true)][string]$Version", File.ReadAllText(Path.Combine(root, "scripts", "package-windows.ps1")));
    }
}
